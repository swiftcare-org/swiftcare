#!/usr/bin/env python3
"""Choose which SwiftCare deployables a CD run releases.

Each deployable is compared with the commit it currently runs: the SHA in a Container
App image tag, or the SHA published by the frontend in version.json. Azure is the source
of truth, so a change missed by a failed or skipped run is picked up by the next run.
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

COMMIT_SHA = re.compile(r"^[0-9a-f]{40}$")
MODES_HELP = "changed, all, or one deployable key"


def load_manifest(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def deployables(manifest):
    """Return every releasable item in a stable order: services, gateway, frontend."""
    items = [dict(service, kind="service") for service in manifest["services"]]
    items.append(dict(manifest["gateway"], kind="gateway"))
    items.append(dict(manifest["frontend"], kind="frontend"))
    return items


def under(path, prefix):
    return path == prefix or path.startswith(prefix.rstrip("/") + "/")


def shared_hit(changed, shared):
    """Return the first changed path that belongs to the shared deployment definition."""
    for path in changed:
        if any(path.startswith(prefix) for prefix in shared.get("exclude_prefixes", [])):
            continue
        if path in shared["files"] or any(path.startswith(prefix) for prefix in shared["prefixes"]):
            return path
    return None


def commit_from_image(image):
    """Return the commit SHA of an image reference such as registry/name:<sha>, or None."""
    if not image or ":" not in image:
        return None
    tag = image.rsplit(":", 1)[1]
    return tag if COMMIT_SHA.match(tag) else None


def git_changed_files(previous, current, repo="."):
    """List files that differ between two commits, or None when either is unknown."""
    for commit in (previous, current):
        known = subprocess.run(
            ["git", "-C", repo, "cat-file", "-e", f"{commit}^{{commit}}"],
            capture_output=True, check=False)
        if known.returncode != 0:
            return None
    result = subprocess.run(
        ["git", "-C", repo, "diff", "--name-only", "-z", previous, current],
        capture_output=True, check=False)
    if result.returncode != 0:
        return None
    return [name for name in result.stdout.decode("utf-8").split("\0") if name]


def previous_commit(item, state):
    """Return (commit, exists) for the running version of a deployable."""
    if item["kind"] == "frontend":
        commit = (state.get("frontend") or {}).get("sha")
        return (commit if commit and COMMIT_SHA.match(commit) else None), commit is not None
    app = (state.get("apps") or {}).get(item["key"]) or {}
    return commit_from_image(app.get("image")), bool(app.get("exists"))


def decide(item, manifest, state, release_sha, changed_files):
    """Return a (selected, reason) pair for one deployable in `changed` mode."""
    previous, exists = previous_commit(item, state)
    if item["kind"] != "frontend" and not exists:
        return True, "app does not exist yet"
    if previous is None:
        return True, "running version is unknown or not a commit"
    app = (state.get("apps") or {}).get(item["key"]) or {}
    if item["kind"] != "frontend" and app.get("ready") is False:
        return True, "latest revision is not ready (an earlier release left it broken)"
    if previous == release_sha:
        return False, "already at the release commit"
    changed = changed_files(previous, release_sha)
    if changed is None:
        return True, "running commit is not in the repository history"
    hit = shared_hit(changed, manifest["shared"])
    if hit:
        return True, f"shared deployment file changed: {hit}"
    own = [path for path in changed if under(path, item["path"])]
    if own:
        return True, f"{len(own)} file(s) changed under {item['path']}"
    return False, f"no changes under {item['path']}"


def select(manifest, state, release_sha, mode, bootstrap_admin, changed_files):
    items = deployables(manifest)
    by_key = {item["key"]: item for item in items}
    if mode not in ("changed", "all") and mode not in by_key:
        raise ValueError(f"unknown mode '{mode}'; use {MODES_HELP}")

    selected = {}
    skipped = {}
    cache = {}

    def cached_changes(previous, current):
        if previous not in cache:
            cache[previous] = changed_files(previous, current)
        return cache[previous]

    for item in items:
        if mode == "all":
            selected[item["key"]] = "manual release of everything"
        elif mode == item["key"]:
            selected[item["key"]] = f"manual release of {item['key']}"
        elif mode == "changed":
            chosen, reason = decide(item, manifest, state, release_sha, cached_changes)
            (selected if chosen else skipped)[item["key"]] = reason
        else:
            skipped[item["key"]] = "not the manually selected deployable"

    if bootstrap_admin and "auth" not in selected:
        selected["auth"] = "administrator bootstrap requested"
        skipped.pop("auth", None)

    # A new service app gets a new internal address that only a new gateway revision learns.
    new_apps = [
        key for key in selected
        if by_key[key]["kind"] == "service" and not previous_commit(by_key[key], state)[1]
    ]
    if new_apps and "gateway" not in selected:
        selected["gateway"] = f"first deployment of {', '.join(new_apps)} needs a gateway address"
        skipped.pop("gateway", None)

    return selected, skipped


def build_result(manifest, state, release_sha, mode, selected, skipped):
    items = {item["key"]: item for item in deployables(manifest)}
    services = [item for item in manifest["services"] if item["key"] in selected]

    def entry(item):
        return {
            "key": item["key"],
            "name": item["name"],
            "image": item["image"],
            "dockerfile": item["dockerfile"],
            "app_var": item["app_var"],
            "migrate_job_var": item.get("migrate_job_var", ""),
            "db_user_var": item.get("db_user_var", ""),
            "db_password_var": item.get("db_password_var", ""),
        }

    images = [entry(item) for item in services]
    if "gateway" in selected:
        images.append(entry(manifest["gateway"]))
    auth = [entry(item) for item in services if item["key"] == "auth"]
    others = [entry(item) for item in services if item["key"] != "auth"]
    # Settings of every service, keyed by display name. The workflow's matrix holds only the
    # names, so GitHub shows readable job names, and each job looks up its settings here.
    meta = {item["name"]: entry(item) for item in manifest["services"]}

    rows = []
    for item in items.values():
        previous, _ = previous_commit(item, state)
        key = item["key"]
        rows.append({
            "key": key,
            "previous": previous[:12] if previous else "unknown",
            "decision": "Release" if key in selected else "Skip",
            "reason": selected.get(key) or skipped.get(key, ""),
        })

    return {
        "release_sha": release_sha,
        "mode": mode,
        "auth": bool(auth),
        "auth_entry": auth,
        "service_meta": meta,
        "services": others,
        "images": images,
        "gateway": "gateway" in selected,
        "frontend": "frontend" in selected,
        "rows": rows,
    }


def summary_markdown(result):
    lines = [
        "## Release selection",
        "",
        f"Release commit `{result['release_sha'][:12]}`, mode `{result['mode']}`.",
        "",
        "| Deployable | Running commit | Decision | Reason |",
        "|---|---|---|---|",
    ]
    for row in result["rows"]:
        lines.append(f"| {row['key']} | `{row['previous']}` | {row['decision']} | {row['reason']} |")
    return "\n".join(lines) + "\n"


def github_outputs(result):
    """Flat key/value pairs for $GITHUB_OUTPUT. Lists and the settings table are compact JSON."""
    return {
        "release_sha": result["release_sha"],
        "auth": str(result["auth"]).lower(),
        "services_list": json.dumps([service["name"] for service in result["services"]], separators=(",", ":")),
        "services_count": str(len(result["services"])),
        "service_meta": json.dumps(result["service_meta"], separators=(",", ":")),
        "images_count": str(len(result["images"])),
        "gateway": str(result["gateway"]).lower(),
        "frontend": str(result["frontend"]).lower(),
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="deployment/cd/services.json")
    parser.add_argument("--state", required=True, help="JSON file written by read-state.sh")
    parser.add_argument("--release-sha", required=True)
    parser.add_argument("--mode", default="changed", help=MODES_HELP)
    parser.add_argument("--bootstrap-admin", action="store_true")
    parser.add_argument("--repo", default=".")
    parser.add_argument("--output", help="write the full result as JSON")
    parser.add_argument("--summary", help="append a Markdown table (for GITHUB_STEP_SUMMARY)")
    parser.add_argument("--github-output", help="append outputs (for GITHUB_OUTPUT)")
    args = parser.parse_args(argv)

    if not COMMIT_SHA.match(args.release_sha):
        parser.error("--release-sha must be a full lowercase commit SHA")

    manifest = load_manifest(args.manifest)
    with open(args.state, encoding="utf-8") as handle:
        state = json.load(handle)

    try:
        selected, skipped = select(
            manifest, state, args.release_sha, args.mode, args.bootstrap_admin,
            lambda previous, current: git_changed_files(previous, current, args.repo))
    except ValueError as error:
        parser.error(str(error))

    result = build_result(manifest, state, args.release_sha, args.mode, selected, skipped)
    print(summary_markdown(result))

    if args.output:
        Path(args.output).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if args.summary:
        with open(args.summary, "a", encoding="utf-8") as handle:
            handle.write(summary_markdown(result))
    if args.github_output:
        with open(args.github_output, "a", encoding="utf-8") as handle:
            for name, value in github_outputs(result).items():
                handle.write(f"{name}={value}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

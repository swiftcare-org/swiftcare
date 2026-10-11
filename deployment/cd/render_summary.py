#!/usr/bin/env python3
"""Render the final release summary from the selection and each job's recorded result."""

import argparse
import json
import sys
from pathlib import Path

REMINDER = (
    "Apps started by this release stay Running. Stop the demo apps after the approved window "
    "and verify `Stopped` (see the operations runbook)."
)


def load_results(directory):
    results = {}
    if directory and Path(directory).is_dir():
        for path in sorted(Path(directory).glob("*.json")):
            data = json.loads(path.read_text(encoding="utf-8"))
            results[data["key"]] = data
    return results


def short(commit):
    return f"`{commit[:12]}`" if commit else "unknown"


def render(selection, results, plan_only=False):
    lines = [
        "## Release result",
        "",
        f"Release commit `{selection['release_sha'][:12]}`, mode `{selection['mode']}`.",
        "",
        "| Deployable | Before | After | Result | Detail |",
        "|---|---|---|---|---|",
    ]
    for row in selection["rows"]:
        key = row["key"]
        before = f"`{row['previous']}`"
        if row["decision"] != "Release":
            cells = (before, "unchanged", "Skipped", row["reason"])
        elif plan_only:
            cells = (before, "-", "Planned", row["reason"])
        elif key in results:
            result = results[key]
            deployed = result["status"] == "Deployed"
            cells = (
                short(result["previous"]) if result["previous"] else before,
                short(result["target"]) if deployed else "see detail",
                result["status"],
                result["detail"],
            )
        else:
            cells = (before, "unchanged", "Not run", "Its image build or an earlier job failed, or it was skipped.")
        lines.append(f"| {key} | " + " | ".join(cells) + " |")
    lines += ["", "Plan only: nothing was changed in Azure." if plan_only else REMINDER, ""]
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--selection", required=True)
    parser.add_argument("--results")
    parser.add_argument("--plan-only", action="store_true")
    args = parser.parse_args(argv)
    selection = json.loads(Path(args.selection).read_text(encoding="utf-8"))
    print(render(selection, load_results(args.results), args.plan_only))
    return 0


if __name__ == "__main__":
    sys.exit(main())

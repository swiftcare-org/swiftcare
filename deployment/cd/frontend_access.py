"""Apply and check the Static Web Apps access policy of a frontend build.

Production keeps the public configuration that Vite copies from frontend/public.
Development replaces it with frontend/swa/staticwebapp.development.json, which limits the whole
site to the dev_team role, and adds the page shown to signed-in users without that role.

Usage: frontend_access.py {apply|check} TARGET DIST_DIR
  apply  copies the development files into DIST_DIR when TARGET is development, then checks.
  check  only validates DIST_DIR/staticwebapp.config.json for TARGET.
Exit status 1 with one message per problem when the build does not match the target.
"""

import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PUBLIC_CONFIG = ROOT / "frontend" / "public" / "staticwebapp.config.json"
DEVELOPMENT_DIR = ROOT / "frontend" / "swa"
CONFIG_NAME = "staticwebapp.config.json"
ROLE = "dev_team"
FALLBACK_PAGE = "access-denied.html"

# Routes that stay readable without the role. Anything else under a development build is gated.
PUBLIC_ROUTES = {"/access-denied.html", "/version.json"}
PUBLIC_FALLBACK = json.loads(PUBLIC_CONFIG.read_text(encoding="utf-8")).get("navigationFallback") or {}
DISABLED_LOGINS ={"/.auth/login/aad", "/.auth/login/twitter"}


def development_errors(config):
    errors = []
    routes = config.get("routes") or []
    by_route = {route.get("route"): route for route in routes}

    # Route rules are not applied to navigationFallback responses, so development has no
    # fallback: React routes are served by an authorized rewrite on the catch-all route.
    if "navigationFallback" in config:
        errors.append("navigationFallback must not be used; its responses bypass route authorization")
    catch_all = by_route.get("/*") or {}
    if catch_all.get("allowedRoles") != [ROLE]:
        errors.append(f"route /* must allow only the {ROLE} role")
    if catch_all.get("rewrite") != "/index.html":
        errors.append("route /* must rewrite to /index.html")
    for route in routes:
        if route.get("rewrite") and route.get("route") != "/*":
            errors.append(f"route {route.get('route')} must not rewrite; only /* serves the SPA")

    # Existing files are matched by their own rules first so they are never rewritten to HTML.
    # The last-route check below guarantees they come before /*.
    for pattern in PUBLIC_FALLBACK.get("exclude") or []:
        if (by_route.get(pattern) or {}).get("allowedRoles") != [ROLE]:
            errors.append(f"static route {pattern} must allow only the {ROLE} role")

    for route in routes:
        name = route.get("route")
        roles = route.get("allowedRoles")
        if roles is not None and roles != [ROLE] and name not in PUBLIC_ROUTES:
            errors.append(f"route {name} grants {roles}; only {sorted(PUBLIC_ROUTES)} may be public")
    for name in PUBLIC_ROUTES:
        if name not in by_route:
            errors.append(f"route {name} is missing")
    for name in DISABLED_LOGINS:
        if (by_route.get(name) or {}).get("statusCode") != 404:
            errors.append(f"sign-in route {name} must return 404")

    # First match wins, so the catch-all must come after every specific rule.
    if routes and routes[-1].get("route") != "/*":
        errors.append("route /* must be the last route")

    overrides = config.get("responseOverrides") or {}
    login = (overrides.get("401") or {})
    if not str(login.get("redirect", "")).startswith("/.auth/login/github") or login.get("statusCode") != 302:
        errors.append("401 must redirect (302) to /.auth/login/github")
    if (overrides.get("403") or {}).get("rewrite") != "/" + FALLBACK_PAGE:
        errors.append(f"403 must rewrite to /{FALLBACK_PAGE}")
    return errors


def production_errors(config):
    errors = []
    if ROLE in json.dumps(config):
        errors.append(f"production must not reference the {ROLE} role")
    for route in config.get("routes") or []:
        if "allowedRoles" in route or "statusCode" in route:
            errors.append(f"production route {route.get('route')} must not restrict access")
    for status in ("401", "403"):
        if status in (config.get("responseOverrides") or {}):
            errors.append(f"production must not override {status} responses")
    return errors


def shared_errors(config):
    """Routing and security headers must match the public configuration in every environment."""
    public = json.loads(PUBLIC_CONFIG.read_text(encoding="utf-8"))
    errors = []
    if config.get("globalHeaders") != public.get("globalHeaders"):
        errors.append(f"globalHeaders differs from frontend/public/{CONFIG_NAME}")
    return errors


def check(target, dist):
    if target not in ("development", "production"):
        return [f"unknown target '{target}'"]
    config_file = Path(dist) / CONFIG_NAME
    if not config_file.is_file():
        return [f"{config_file} is missing from the build output"]
    try:
        config = json.loads(config_file.read_text(encoding="utf-8"))
    except ValueError as error:
        return [f"{config_file} is not valid JSON: {error}"]
    errors = shared_errors(config)
    if target == "development":
        errors += development_errors(config)
        if not (Path(dist) / FALLBACK_PAGE).is_file():
            errors.append(f"{FALLBACK_PAGE} is missing from the build output")
    else:
        errors += production_errors(config)
    return errors


def apply(target, dist):
    if target == "development":
        dist = Path(dist)
        dist.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(DEVELOPMENT_DIR / "staticwebapp.development.json", dist / CONFIG_NAME)
        shutil.copyfile(DEVELOPMENT_DIR / FALLBACK_PAGE, dist / FALLBACK_PAGE)
    return check(target, dist)


def main(argv):
    if len(argv) != 4 or argv[1] not in ("apply", "check"):
        print(__doc__.strip().split("\n\n")[-1], file=sys.stderr)
        return 2
    action, target, dist = argv[1:]
    errors = (apply if action == "apply" else check)(target, dist)
    for error in errors:
        print(f"::error::{error}")
    if not errors:
        print(f"Frontend access policy for {target}: OK")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

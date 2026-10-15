import copy
import json
import re
import tempfile
import unittest
from pathlib import Path

import frontend_access as fa

ROOT = fa.ROOT
DEV = json.loads((fa.DEVELOPMENT_DIR / "staticwebapp.development.json").read_text(encoding="utf-8"))
PUBLIC = json.loads(fa.PUBLIC_CONFIG.read_text(encoding="utf-8"))
CD = (ROOT / ".github" / "workflows" / "cd.yml").read_text(encoding="utf-8").replace("\r\n", "\n")


def build(config, with_page=True):
    """A fake dist folder holding the given configuration."""
    folder = tempfile.TemporaryDirectory()
    (Path(folder.name) / fa.CONFIG_NAME).write_text(json.dumps(config), encoding="utf-8")
    if with_page:
        (Path(folder.name) / fa.FALLBACK_PAGE).write_text("denied", encoding="utf-8")
    return folder


class DevelopmentPolicyTests(unittest.TestCase):
    def test_shipped_development_files_pass(self):
        with tempfile.TemporaryDirectory() as dist:
            self.assertEqual(fa.apply("development", dist), [])

    def test_catch_all_requires_the_role_and_serves_the_spa(self):
        routes = {route["route"]: route for route in DEV["routes"]}
        self.assertEqual(routes["/*"]["allowedRoles"], ["dev_team"])
        self.assertEqual(routes["/*"]["rewrite"], "/index.html")
        self.assertEqual(DEV["routes"][-1]["route"], "/*")

    def test_no_navigation_fallback_because_it_skips_route_rules(self):
        self.assertNotIn("navigationFallback", DEV)
        config = copy.deepcopy(DEV)
        config["navigationFallback"] = PUBLIC["navigationFallback"]
        with build(config) as dist:
            self.assertTrue(any("navigationFallback" in e for e in fa.check("development", dist)))

    def test_existing_static_files_are_gated_and_not_rewritten(self):
        routes = {route["route"]: route for route in DEV["routes"]}
        for pattern in PUBLIC["navigationFallback"]["exclude"]:
            self.assertEqual(routes[pattern]["allowedRoles"], ["dev_team"], pattern)
            self.assertNotIn("rewrite", routes[pattern], pattern)
            self.assertLess(list(routes).index(pattern), list(routes).index("/*"), pattern)

    def test_a_missing_static_rule_is_rejected(self):
        config = copy.deepcopy(DEV)
        config["routes"] = [r for r in config["routes"] if r["route"] != "/assets/*"]
        with build(config) as dist:
            self.assertTrue(any("/assets/*" in e for e in fa.check("development", dist)))

    def test_a_rewrite_on_a_static_rule_is_rejected(self):
        config = copy.deepcopy(DEV)
        config["routes"][4]["rewrite"] = "/index.html"
        with build(config) as dist:
            self.assertTrue(any("must not rewrite" in e for e in fa.check("development", dist)))

    def test_auth_endpoints_are_not_overridden(self):
        # Sign-in, callback and sign-out stay platform-handled; only the two unused providers are blocked.
        for route in DEV["routes"]:
            if route["route"].startswith("/.auth"):
                self.assertIn(route["route"], fa.DISABLED_LOGINS)
                self.assertNotIn("rewrite", route)

    def test_only_the_listed_routes_are_public(self):
        public = {r["route"] for r in DEV["routes"] if "anonymous" in r.get("allowedRoles", [])}
        self.assertEqual(public, fa.PUBLIC_ROUTES)

    def test_unauthenticated_visitors_are_sent_to_github(self):
        self.assertEqual(DEV["responseOverrides"]["401"]["statusCode"], 302)
        self.assertTrue(DEV["responseOverrides"]["401"]["redirect"].startswith("/.auth/login/github"))

    def test_other_sign_in_providers_are_disabled(self):
        disabled = {r["route"] for r in DEV["routes"] if r.get("statusCode") == 404}
        self.assertEqual(disabled, fa.DISABLED_LOGINS)

    def test_headers_match_production(self):
        self.assertEqual(DEV["globalHeaders"], PUBLIC["globalHeaders"])

    def test_a_catch_all_before_a_specific_rule_is_rejected(self):
        config = copy.deepcopy(DEV)
        config["routes"].reverse()
        with build(config) as dist:
            self.assertTrue(any("last route" in e for e in fa.check("development", dist)))

    def test_a_widened_route_is_rejected(self):
        config = copy.deepcopy(DEV)
        config["routes"][-1]["allowedRoles"] = ["authenticated"]
        with build(config) as dist:
            self.assertTrue(fa.check("development", dist))

    def test_production_keeps_its_navigation_fallback(self):
        self.assertIn("navigationFallback", PUBLIC)

    def test_public_config_is_rejected_for_development(self):
        with build(PUBLIC) as dist:
            self.assertTrue(fa.check("development", dist))

    def test_missing_denied_page_is_rejected(self):
        with build(DEV, with_page=False) as dist:
            self.assertTrue(any(fa.FALLBACK_PAGE in e for e in fa.check("development", dist)))

    def test_changed_security_headers_are_rejected(self):
        config = copy.deepcopy(DEV)
        config["globalHeaders"]["X-Frame-Options"] = "SAMEORIGIN"
        with build(config) as dist:
            self.assertTrue(any("globalHeaders" in e for e in fa.check("development", dist)))


class ProductionPolicyTests(unittest.TestCase):
    def test_public_config_is_open(self):
        self.assertEqual(fa.production_errors(PUBLIC), [])
        with tempfile.TemporaryDirectory() as dist:
            (Path(dist) / fa.CONFIG_NAME).write_text(json.dumps(PUBLIC), encoding="utf-8")
            self.assertEqual(fa.apply("production", dist), [])

    def test_apply_leaves_the_production_build_unchanged(self):
        with tempfile.TemporaryDirectory() as dist:
            (Path(dist) / fa.CONFIG_NAME).write_text(json.dumps(PUBLIC), encoding="utf-8")
            fa.apply("production", dist)
            self.assertEqual(json.loads((Path(dist) / fa.CONFIG_NAME).read_text(encoding="utf-8")), PUBLIC)
            self.assertFalse((Path(dist) / fa.FALLBACK_PAGE).exists())

    def test_development_config_is_rejected_for_production(self):
        with build(DEV) as dist:
            self.assertTrue(fa.check("production", dist))

    def test_unknown_target_and_missing_config_fail(self):
        with tempfile.TemporaryDirectory() as dist:
            self.assertTrue(fa.check("production", dist))
            self.assertTrue(fa.check("staging", dist))


class WorkflowWiringTests(unittest.TestCase):
    def test_frontend_job_applies_the_policy_between_build_and_deploy(self):
        job = CD[CD.index("  deploy-frontend:"):CD.index("\n  summary:")]
        apply = job.index("frontend_access.py apply")
        self.assertLess(job.index("npm run build"), apply)
        self.assertLess(apply, job.index("Azure/static-web-apps-deploy"))
        self.assertIn("needs.target.outputs.target", job)

    def test_vite_never_copies_the_development_files(self):
        self.assertFalse(re.search(r"swa", (ROOT / "frontend" / "vite.config.ts").read_text(encoding="utf-8")))
        self.assertFalse((ROOT / "frontend" / "public" / fa.FALLBACK_PAGE).exists())


if __name__ == "__main__":
    unittest.main()

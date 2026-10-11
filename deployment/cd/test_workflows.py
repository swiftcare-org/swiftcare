"""Structural checks on the CD workflow that a YAML linter does not catch."""

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = json.loads((ROOT / "deployment" / "cd" / "services.json").read_text(encoding="utf-8"))
CD = (ROOT / ".github" / "workflows" / "cd.yml").read_text(encoding="utf-8").replace("\r\n", "\n")


def jobs(text):
    """Return {job id: job block} for the top-level jobs of a workflow."""
    body = text[text.index("\njobs:\n") + len("\njobs:\n"):]
    blocks = re.split(r"(?m)^(?=  [A-Za-z0-9_-]+:\n)", body)
    return {block.split(":", 1)[0].strip(): block for block in blocks if block.startswith("  ") and ":" in block}


JOBS = jobs(CD)


class CdWorkflowTests(unittest.TestCase):
    def test_expected_jobs_exist(self):
        for job in ("plan", "deploy-auth", "deploy-services", "gateway-image", "deploy-gateway",
                    "deploy-frontend", "summary"):
            self.assertIn(job, JOBS)

    def test_job_names_have_no_expressions(self):
        # A skipped job shows its raw name template, so names stay plain text.
        for job, block in JOBS.items():
            name = re.search(r"(?m)^    name: (.*)$", block)
            if name:
                self.assertNotIn("${{", name.group(1), job)

    def test_jobs_that_read_environment_secrets_declare_the_environment(self):
        # Environment secrets are empty in a job that does not name the environment.
        for job, block in JOBS.items():
            names = set(re.findall(r"secrets(?:\.([A-Z0-9_]+)|\[)", block)) - {"GITHUB_TOKEN"}
            if names:
                self.assertIn("    environment: azure-development\n", block, job)

    def test_only_the_ci_quality_gate_is_called_as_a_workflow(self):
        # A called workflow does not receive environment secrets, so deploy steps stay in cd.yml.
        called = re.findall(r"(?m)^    uses: (\./\.github/workflows/\S+)$", CD)
        self.assertEqual(called, ["./.github/workflows/ci.yml"])

    def test_service_jobs_read_settings_from_the_plan_table(self):
        for job in ("deploy-auth", "deploy-services"):
            block = JOBS[job]
            self.assertIn("needs.plan.outputs.service_meta", block)
            self.assertIn("secrets[fromJSON(needs.plan.outputs.service_meta)", block)

    def test_services_matrix_holds_only_names(self):
        block = JOBS["deploy-services"]
        self.assertIn("service: ${{ fromJSON(needs.plan.outputs.services_list || '[]') }}", block)
        self.assertNotIn("include", block.split("steps:")[0])

    def test_every_app_name_variable_is_set_in_each_job_that_resolves_apps(self):
        # The scripts read each Container App name from an environment variable named in the
        # manifest, so a service missing from one job's env block fails only at run time.
        variables = [service["app_var"] for service in MANIFEST["services"]] + [MANIFEST["gateway"]["app_var"]]
        for job in ("plan", "deploy-auth", "deploy-services", "deploy-gateway"):
            for variable in variables:
                self.assertIn(f"${{{{ vars.{variable} }}}}", JOBS[job], f"{job} lacks {variable}")

    def test_manual_choices_cover_every_deployable(self):
        keys = [service["key"] for service in MANIFEST["services"]] + ["gateway"]
        for input_name, extra in (("release", ["changed", "all", "frontend"]), ("rollback_drill", ["none"])):
            block = CD[CD.index(f"      {input_name}:\n"):]
            block = block.split("        options:\n", 1)[1]
            options = re.findall(r"^          - (\S+)$", block.split("\n\n", 1)[0], re.M)
            for key in keys + extra:
                self.assertIn(key, options, f"{input_name} lacks {key}")


if __name__ == "__main__":
    unittest.main()

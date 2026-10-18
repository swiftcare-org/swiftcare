"""Structural checks on the CD and window guard workflows that a YAML linter does not catch."""

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = json.loads((ROOT / "deployment" / "cd" / "services.json").read_text(encoding="utf-8"))
CD = (ROOT / ".github" / "workflows" / "cd.yml").read_text(encoding="utf-8").replace("\r\n", "\n")
GUARD = (ROOT / ".github" / "workflows" / "window-guard.yml").read_text(encoding="utf-8").replace("\r\n", "\n")


def jobs(text):
    """Return {job id: job block} for the top-level jobs of a workflow."""
    body = text[text.index("\njobs:\n") + len("\njobs:\n"):]
    blocks = re.split(r"(?m)^(?=  [A-Za-z0-9_-]+:\n)", body)
    return {block.split(":", 1)[0].strip(): block for block in blocks if block.startswith("  ") and ":" in block}


JOBS = jobs(CD)
ENVIRONMENT_LINE = "    environment: ${{ needs.target.outputs.environment }}\n"


class CdWorkflowTests(unittest.TestCase):
    def test_expected_jobs_exist(self):
        for job in ("target", "plan", "deploy-auth", "deploy-services", "gateway-image", "deploy-gateway",
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
                self.assertIn(ENVIRONMENT_LINE, block, job)

    def test_environment_jobs_wait_for_the_target_job(self):
        # needs.target is empty in a job that does not list it, which would leave the job
        # without an Environment and without its secrets.
        for job, block in JOBS.items():
            if ENVIRONMENT_LINE in block:
                needs = re.search(r"(?m)^    needs: (.*)$", block)
                self.assertIsNotNone(needs, job)
                self.assertIn("target", needs.group(1), job)

    def test_target_job_runs_before_any_environment(self):
        # A closed demo window must end the run before any approval request or Azure step.
        block = JOBS["target"]
        self.assertIsNone(re.search(r"(?m)^    environment:", block))
        self.assertNotIn("secrets.", block)
        self.assertIn("bash deployment/cd/resolve-target.sh", block)
        self.assertIn("vars.DEMO_WINDOW_DEVELOPMENT", block)
        self.assertIn("vars.DEMO_WINDOW_PRODUCTION", block)
        self.assertIn("needs.target.outputs.proceed == 'true'", JOBS["plan"])
        self.assertIn("needs.target.outputs.proceed == 'true'", JOBS["manual-quality-gate"])

    def test_both_branches_trigger_and_each_environment_has_its_own_queue(self):
        trigger = CD[CD.index("  workflow_run:\n"):CD.index("  workflow_dispatch:\n")]
        self.assertIn("      - main\n", trigger)
        self.assertIn("      - develop\n", trigger)
        # Pull request CI from develop into main reports head branch develop; only pushes deploy.
        self.assertIn("github.event.workflow_run.event == 'push'", JOBS["target"])
        group = re.search(r"(?m)^  group: (.*)$", CD).group(1)
        self.assertIn("'azure-production'", group)
        self.assertIn("'azure-development'", group)

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

    def test_target_job_reads_both_window_end_times(self):
        block = JOBS["target"]
        self.assertIn("vars.DEMO_WINDOW_DEVELOPMENT_UNTIL", block)
        self.assertIn("vars.DEMO_WINDOW_PRODUCTION_UNTIL", block)

    def test_data_check_knows_the_kafka_app(self):
        self.assertIn("KAFKA_APP: ${{ vars.KAFKA_APP }}", JOBS["plan"])

    def test_plan_knows_the_frontend_host(self):
        self.assertIn("FRONTEND_HOST: ${{ vars.FRONTEND_HOST }}", JOBS["plan"])
        self.assertIn("AZURE_FRONTEND_APP: ${{ vars.AZURE_FRONTEND_APP }}", JOBS["plan"])

    def test_each_frontend_step_belongs_to_one_host(self):
        # Both hosts share one job, so every step after the configuration check names its host;
        # only the result recording runs for both.
        block = JOBS["deploy-frontend"]
        steps = block.split("\n      - name: ")[1:]
        shared = {"Check out the release commit", "Validate configuration", "Record the result", "Upload the result"}
        for step in steps:
            name = step.split("\n", 1)[0]
            if name in shared:
                continue
            self.assertRegex(step, r"if: env\.FRONTEND_HOST (==|!=) 'container_app'", name)

    def test_container_app_frontend_is_built_for_the_gateway_and_the_release(self):
        block = JOBS["deploy-frontend"]
        self.assertIn("file: frontend/Dockerfile", block)
        self.assertIn("VITE_GATEWAY_URL=${{ vars.GATEWAY_ORIGIN }}", block)
        self.assertIn("RELEASE_SHA=${{ needs.plan.outputs.release_sha }}", block)
        self.assertIn("bash deployment/cd/deploy-frontend.sh", block)


GUARD_JOBS = jobs(GUARD)


class WindowGuardWorkflowTests(unittest.TestCase):
    def test_runs_hourly_and_on_demand(self):
        self.assertRegex(GUARD, r'(?m)^    - cron: "\d+ \* \* \* \*"$')
        self.assertIn("  workflow_dispatch:\n", GUARD)

    def test_manual_runs_default_to_a_dry_run(self):
        block = GUARD[GUARD.index("      dry_run:\n"):]
        self.assertIn("        default: true", block.split("\n\n", 1)[0])

    def test_workflow_grants_nothing_by_default(self):
        self.assertIn("\npermissions: {}\n", GUARD)

    def test_decision_needs_no_azure_and_no_environment(self):
        block = GUARD_JOBS["decide"]
        self.assertIsNone(re.search(r"(?m)^    environment:", block))
        self.assertNotIn("id-token", block)
        self.assertNotIn("secrets.", block)
        self.assertIn("vars.DEMO_WINDOW_PRODUCTION_UNTIL", block)
        self.assertIn("bash deployment/cd/window-guard.sh decide", block)

    def test_guard_uses_only_its_own_environment_and_no_secrets(self):
        # The deployment Environment holds every secret and needs a reviewer; the guard must not use it.
        block = GUARD_JOBS["guard"]
        self.assertIn("    environment: azure-production-guard\n", block)
        self.assertNotIn("secrets.", GUARD)
        self.assertNotIn("environment: azure-production\n", GUARD)

    def test_guard_stops_only_after_the_decision(self):
        block = GUARD_JOBS["guard"]
        self.assertIn("    needs: decide\n", block)
        stop = block[block.index("      - name: Stop resources"):].split("\n      - name:", 1)[0]
        self.assertIn("if: needs.decide.outputs.act == 'true'", stop)

    def test_guard_leaves_the_public_frontend_running(self):
        self.assertIn("FRONTEND_APP: ${{ vars.AZURE_FRONTEND_APP }}", GUARD_JOBS["guard"])

    def test_scheduled_runs_are_never_dry_runs(self):
        self.assertIn("DRY_RUN: ${{ github.event_name == 'workflow_dispatch' && inputs.dry_run || false }}",
                      GUARD_JOBS["guard"])


if __name__ == "__main__":
    unittest.main()

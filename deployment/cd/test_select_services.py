import json
import subprocess
import tempfile
import unittest
from pathlib import Path

import select_services as sel

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = sel.load_manifest(ROOT / "deployment" / "cd" / "services.json")

OLD = "a" * 40
NEW = "b" * 40
IMAGE = "ghcr.io/swiftcare-org/{name}:{sha}"
SERVICE_KEYS = [service["key"] for service in MANIFEST["services"]]
BACKEND_KEYS = SERVICE_KEYS + ["gateway"]
ALL_KEYS = BACKEND_KEYS + ["frontend"]


def state(sha=OLD, missing=(), frontend=OLD, tag=None):
    apps = {}
    for item in MANIFEST["services"] + [MANIFEST["gateway"]]:
        image = tag if tag is not None else IMAGE.format(name=item["image"], sha=sha)
        apps[item["key"]] = {"exists": item["key"] not in missing, "image": image, "running": "Stopped"}
        if item["key"] in missing:
            apps[item["key"]]["image"] = None
    return {"apps": apps, "frontend": {"sha": frontend}}


def run(files, mode="changed", bootstrap=False, current=None):
    selected, skipped = sel.select(
        MANIFEST, current or state(), NEW, mode, bootstrap, lambda previous, release: files)
    return selected, skipped


class SelectionTests(unittest.TestCase):
    def test_one_service_change_selects_only_that_service(self):
        selected, skipped = run(["services/PrescriptionService/Controllers/PrescriptionsController.cs"])
        self.assertEqual(list(selected), ["prescription"])
        self.assertEqual(set(skipped), set(ALL_KEYS) - {"prescription"})

    def test_notification_change_selects_only_notification(self):
        selected, skipped = run(["services/NotificationService/Services/NotificationFeedService.cs"])
        self.assertEqual(list(selected), ["notification"])
        self.assertEqual(set(skipped), set(ALL_KEYS) - {"notification"})

    def test_gateway_change_selects_only_the_gateway(self):
        selected, _ = run(["ApiGateway/appsettings.json"])
        self.assertEqual(list(selected), ["gateway"])

    def test_frontend_change_selects_only_the_frontend(self):
        selected, _ = run(["frontend/src/App.tsx", "frontend/public/staticwebapp.config.json"])
        self.assertEqual(list(selected), ["frontend"])

    def test_two_services_are_selected_together(self):
        selected, _ = run(["services/AuthService/Program.cs", "services/QueueService/Program.cs"])
        self.assertEqual(set(selected), {"auth", "queue"})

    def test_cd_workflow_selects_everything(self):
        selected, _ = run([".github/workflows/cd.yml"])
        self.assertEqual(set(selected), set(ALL_KEYS))

    def test_release_scripts_and_manifest_select_everything(self):
        for path in ("deployment/cd/smoke.sh", "deployment/cd/services.json", ".dockerignore"):
            with self.subTest(path=path):
                selected, _ = run([path])
                self.assertEqual(set(selected), set(ALL_KEYS))

    def test_release_script_tests_select_nothing(self):
        selected, _ = run(["deployment/cd/test_select_services.py"])
        self.assertEqual(selected, {})

    def test_files_outside_deployables_select_nothing(self):
        for path in (
            "deployment/terraform/main.tf", "docs/guide.md", "tests/E2ETests/Foo.cs", "README.md",
            ".github/workflows/ci.yml", "docker-compose.yml", "deployment/docker/mysql-init/01-create-databases.sh",
            "SwiftCare.slnx",
        ):
            with self.subTest(path=path):
                selected, skipped = run([path])
                self.assertEqual(selected, {})
                self.assertEqual(set(skipped), set(ALL_KEYS))

    def test_path_prefix_requires_a_directory_boundary(self):
        selected, _ = run(["frontend-notes.md", "ApiGatewayExtras/file.cs"])
        self.assertEqual(selected, {})

    def test_app_already_at_release_commit_is_skipped(self):
        selected, skipped = sel.select(
            MANIFEST, state(sha=NEW, frontend=NEW), NEW, "changed", False,
            lambda previous, release: self.fail("no diff is needed"))
        self.assertEqual(selected, {})
        self.assertIn("already at the release commit", skipped["auth"])

    def test_app_whose_latest_revision_is_not_ready_is_selected_again(self):
        current = state(sha=NEW, frontend=NEW)
        current["apps"]["queue"]["ready"] = False
        selected, skipped = sel.select(
            MANIFEST, current, NEW, "changed", False, lambda previous, release: self.fail("no diff is needed"))
        self.assertEqual(list(selected), ["queue"])
        self.assertIn("not ready", selected["queue"])
        self.assertIn("already at the release commit", skipped["patient"])

    def test_state_without_readiness_counts_as_ready(self):
        selected, _ = sel.select(MANIFEST, state(sha=NEW, frontend=NEW), NEW, "changed", False, lambda p, r: [])
        self.assertEqual(selected, {})

    def test_unknown_image_tag_selects_the_app(self):
        for tag in ("ghcr.io/x/swiftcare-auth:latest", "ghcr.io/x/swiftcare-auth", "ghcr.io/x/swiftcare-auth:ABCDEF"):
            with self.subTest(tag=tag):
                selected, _ = run([], current=state(tag=tag))
                self.assertEqual(set(selected), set(BACKEND_KEYS))

    def test_missing_service_app_selects_it_and_the_gateway(self):
        selected, _ = run([], current=state(missing=("patient",)))
        self.assertEqual(set(selected), {"patient", "gateway"})
        self.assertIn("patient", selected["gateway"])

    def test_missing_gateway_app_selects_only_the_gateway(self):
        selected, _ = run([], current=state(missing=("gateway",)))
        self.assertEqual(list(selected), ["gateway"])

    def test_commit_missing_from_history_selects_the_app(self):
        selected, _ = sel.select(MANIFEST, state(), NEW, "changed", False, lambda previous, release: None)
        self.assertEqual(set(selected), set(ALL_KEYS))

    def test_frontend_without_a_published_commit_is_selected(self):
        selected, _ = run([], current=state(frontend=None))
        self.assertEqual(list(selected), ["frontend"])

    def test_each_distinct_previous_commit_is_diffed_once(self):
        calls = []

        def changed(previous, release):
            calls.append(previous)
            return []

        sel.select(MANIFEST, state(), NEW, "changed", False, changed)
        self.assertEqual(calls, [OLD])

    def test_different_running_commits_are_each_compared(self):
        current = state()
        current["apps"]["queue"]["image"] = IMAGE.format(name="swiftcare-queue", sha="c" * 40)
        seen = {}

        def changed(previous, release):
            seen[previous] = True
            return ["services/QueueService/Program.cs"] if previous == "c" * 40 else []

        selected, _ = sel.select(MANIFEST, current, NEW, "changed", False, changed)
        self.assertEqual(list(selected), ["queue"])
        self.assertEqual(set(seen), {OLD, "c" * 40})


class ManualModeTests(unittest.TestCase):
    def test_all_selects_every_deployable_even_without_changes(self):
        selected, skipped = run([], mode="all")
        self.assertEqual(set(selected), set(ALL_KEYS))
        self.assertEqual(skipped, {})

    def test_single_service_selects_only_it(self):
        for key in SERVICE_KEYS:
            with self.subTest(key=key):
                selected, skipped = run(["ApiGateway/Program.cs"], mode=key)
                self.assertEqual(list(selected), [key])
                self.assertEqual(set(skipped), set(ALL_KEYS) - {key})

    def test_gateway_and_frontend_can_be_released_alone(self):
        for key in ("gateway", "frontend"):
            with self.subTest(key=key):
                selected, _ = run([], mode=key)
                self.assertEqual(list(selected), [key])

    def test_single_service_does_not_diff(self):
        sel.select(MANIFEST, state(), NEW, "prescription", False,
                   lambda previous, release: self.fail("manual mode must not diff"))

    def test_single_new_service_also_selects_the_gateway(self):
        selected, _ = run([], mode="queue", current=state(missing=("queue",)))
        self.assertEqual(set(selected), {"queue", "gateway"})

    def test_unknown_mode_is_rejected(self):
        with self.assertRaises(ValueError):
            run([], mode="billing")

    def test_bootstrap_adds_auth_to_a_release_that_skips_it(self):
        selected, skipped = run(["services/QueueService/Program.cs"], bootstrap=True)
        self.assertEqual(set(selected), {"auth", "queue"})
        self.assertNotIn("auth", skipped)

    def test_bootstrap_with_a_single_other_service_adds_auth(self):
        selected, _ = run([], mode="patient", bootstrap=True)
        self.assertEqual(set(selected), {"auth", "patient"})


class OutputTests(unittest.TestCase):
    def build(self, files, **kwargs):
        selected, skipped = run(files, **kwargs)
        return sel.build_result(MANIFEST, state(), NEW, kwargs.get("mode", "changed"), selected, skipped)

    def test_auth_is_separated_from_the_other_services(self):
        result = self.build([], mode="all")
        self.assertTrue(result["auth"])
        self.assertNotIn("auth", [service["key"] for service in result["services"]])
        self.assertEqual(
            {service["key"] for service in result["services"]}, set(SERVICE_KEYS) - {"auth"})

    def test_images_cover_services_and_gateway_but_not_the_frontend(self):
        result = self.build([], mode="all")
        self.assertEqual({image["key"] for image in result["images"]}, set(BACKEND_KEYS))

    def test_a_frontend_only_release_builds_no_images(self):
        result = self.build(["frontend/src/App.tsx"])
        self.assertEqual(result["images"], [])
        self.assertFalse(result["auth"])
        self.assertTrue(result["frontend"])
        self.assertFalse(result["gateway"])

    def test_smoke_roles_are_known_roles(self):
        for service in MANIFEST["services"]:
            self.assertTrue(service["smoke_roles"], service["key"])
            self.assertTrue(set(service["smoke_roles"]) <= {"RECEPTIONIST", "DOCTOR"}, service["key"])

    def test_auth_is_kept_out_of_the_services_list(self):
        outputs = sel.github_outputs(self.build([], mode="all"))
        self.assertEqual(outputs["auth"], "true")
        self.assertNotIn("AuthService", json.loads(outputs["services_list"]))
        outputs = sel.github_outputs(self.build([], mode="queue"))
        self.assertEqual(outputs["auth"], "false")
        self.assertEqual(json.loads(outputs["services_list"]), ["QueueService"])

    def test_settings_table_covers_every_service_by_display_name(self):
        meta = json.loads(sel.github_outputs(self.build([], mode="queue"))["service_meta"])
        self.assertEqual(set(meta), {service["name"] for service in MANIFEST["services"]})
        self.assertEqual(meta["AuthService"]["key"], "auth")
        self.assertEqual(meta["QueueService"]["migrate_job_var"], "AZURE_QUEUE_MIGRATE_JOB")
        self.assertEqual(meta["QueueService"]["db_password_var"], "QUEUE_DB_PASSWORD")
        self.assertEqual(meta["QueueService"]["dockerfile"], "services/QueueService/Dockerfile")

    def test_github_outputs_are_valid_json(self):
        outputs = sel.github_outputs(self.build(["services/QueueService/Program.cs"]))
        self.assertEqual(outputs["auth"], "false")
        self.assertEqual(outputs["services_count"], "1")
        self.assertEqual(json.loads(outputs["services_list"]), ["QueueService"])
        self.assertEqual(outputs["images_count"], "1")
        self.assertEqual(outputs["gateway"], "false")
        self.assertEqual(outputs["frontend"], "false")

    def test_summary_lists_every_deployable(self):
        text = sel.summary_markdown(self.build(["ApiGateway/Program.cs"]))
        for key in ALL_KEYS:
            self.assertIn(f"| {key} |", text)
        self.assertIn("| gateway | `aaaaaaaaaaaa` | Release |", text)


class GitTests(unittest.TestCase):
    def git(self, repo, *args):
        subprocess.run(["git", "-C", repo, *args], check=True, capture_output=True)

    def test_changed_files_between_real_commits(self):
        with tempfile.TemporaryDirectory() as repo:
            self.git(repo, "init", "-q")
            self.git(repo, "config", "user.email", "ci@example.test")
            self.git(repo, "config", "user.name", "ci")
            (Path(repo) / "a.txt").write_text("one")
            self.git(repo, "add", ".")
            self.git(repo, "commit", "-q", "-m", "first")
            first = subprocess.run(["git", "-C", repo, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
            (Path(repo) / "dir with space").mkdir()
            (Path(repo) / "dir with space" / "b.txt").write_text("two")
            self.git(repo, "add", ".")
            self.git(repo, "commit", "-q", "-m", "second")
            second = subprocess.run(["git", "-C", repo, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()

            self.assertEqual(sel.git_changed_files(first, second, repo), ["dir with space/b.txt"])
            self.assertEqual(sel.git_changed_files(second, second, repo), [])
            self.assertIsNone(sel.git_changed_files("f" * 40, second, repo))


class ManifestTests(unittest.TestCase):
    def test_commit_from_image(self):
        self.assertEqual(sel.commit_from_image(IMAGE.format(name="x", sha=OLD)), OLD)
        self.assertIsNone(sel.commit_from_image(None))
        self.assertIsNone(sel.commit_from_image("registry:5000/name"))
        self.assertIsNone(sel.commit_from_image("name:" + "A" * 40))

    def test_keys_ports_and_names_are_unique(self):
        items = MANIFEST["services"] + [MANIFEST["gateway"]]
        for field in ("key", "name", "port", "image", "app_var"):
            values = [item[field] for item in items]
            self.assertEqual(len(values), len(set(values)), field)
        databases = [service["database"] for service in MANIFEST["services"]]
        self.assertEqual(len(databases), len(set(databases)))

    def test_service_entries_have_every_required_field(self):
        required = {
            "key", "name", "path", "image", "dockerfile", "port", "database", "app_var",
            "migrate_job_var", "db_user_var", "db_password_var", "connection_setting",
            "db_secret", "gateway_cluster", "gateway_destination", "extra_secrets", "extra_env",
            "smoke_roles",
        }
        for service in MANIFEST["services"]:
            self.assertEqual(required - set(service), set(), service["key"])

    def test_auth_is_a_service_entry(self):
        self.assertIn("auth", SERVICE_KEYS)

    def test_every_dockerfile_in_the_repository_has_a_manifest_entry(self):
        in_repo = {
            str(path.relative_to(ROOT)).replace("\\", "/")
            for path in list((ROOT / "services").glob("*/Dockerfile")) + [ROOT / "ApiGateway" / "Dockerfile"]
        }
        in_manifest = {item["dockerfile"] for item in MANIFEST["services"] + [MANIFEST["gateway"]]}
        self.assertEqual(in_repo, in_manifest)

    def test_shared_files_and_prefixes_exist(self):
        for path in MANIFEST["shared"]["files"]:
            self.assertTrue((ROOT / path).is_file(), path)
        for prefix in MANIFEST["shared"]["prefixes"]:
            self.assertTrue((ROOT / prefix).is_dir(), prefix)

    def test_manifest_paths_exist(self):
        for item in MANIFEST["services"] + [MANIFEST["gateway"], MANIFEST["frontend"]]:
            self.assertTrue((ROOT / item["path"]).is_dir(), item["path"])

    def test_ports_match_the_dockerfiles(self):
        for item in MANIFEST["services"] + [MANIFEST["gateway"]]:
            text = (ROOT / item["dockerfile"]).read_text(encoding="utf-8")
            self.assertIn(str(item["port"]), text, item["key"])

    def test_gateway_clusters_exist_in_the_gateway_settings(self):
        clusters = json.loads((ROOT / "ApiGateway" / "appsettings.json").read_text(encoding="utf-8"))["ReverseProxy"]["Clusters"]
        for service in MANIFEST["services"]:
            cluster = clusters[service["gateway_cluster"]]
            self.assertIn(service["gateway_destination"], cluster["Destinations"], service["key"])


if __name__ == "__main__":
    unittest.main()

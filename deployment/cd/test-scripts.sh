#!/usr/bin/env bash
# Offline tests of the CD scripts against a fake Azure CLI (testdata/fake-az). They prove script
# logic: which apps a step touches, argument construction, failure handling and rollback scope.
# They do not prove Azure behavior; that needs a real deployment.
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

mkdir -p "$WORK/bin"
cp "$HERE/testdata/fake-az" "$WORK/bin/az"
chmod +x "$WORK/bin/az"
REAL_CURL="$(command -v curl)"
export REAL_CURL
cat > "$WORK/bin/curl" <<'EOF'
#!/usr/bin/env bash
# Requests to the local stub gateway go to the real curl; anything else is the fake frontend.
case "$*" in *127.0.0.1*) exec "$REAL_CURL" "$@" ;; esac
[ -n "${FAKE_CURL_BODY:-}" ] && { printf '%s' "$FAKE_CURL_BODY"; exit 0; }
exit 22
EOF
chmod +x "$WORK/bin/curl"
export PATH="$WORK/bin:$PATH"
export FAKE_AZ_STATE="$WORK/state" FAKE_AZ_LOG="$WORK/az.log" GITHUB_OUTPUT="$WORK/output"
export CD_WAIT_ATTEMPTS=2 CD_WAIT_INTERVAL=0

OLD=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
NEW=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
export RESOURCE_GROUP=rg CONTAINERAPPS_ENVIRONMENT=env MYSQL_FQDN=mysql.example.test
export GHCR_USERNAME=user GHCR_TOKEN=token IMAGE_REPOSITORY=ghcr.io/org DEPLOY_SHA=$NEW
export SERVICE_DB_USER=dbuser SERVICE_DB_PASSWORD=dbpass
export JWT_ISSUER=issuer JWT_AUDIENCE=audience JWT_SIGNING_KEY=dummy-dummy-dummy-dummy-dummy-dummy-dummy
export GATEWAY_INTERNAL_SECRET=dummy-dummy-dummy KAFKA_BOOTSTRAP_SERVERS=kafka.example.test:9092
export KAFKA_PATIENT_CHECKED_IN_TOPIC=patient-checked-in KAFKA_QUEUE_CONSUMER_GROUP=queue-group
export FRONTEND_ORIGIN=https://app.example.test FRONTEND_WWW_ORIGIN=https://www.example.test
export AZURE_AUTH_APP=swiftcare-auth AZURE_PATIENT_APP=swiftcare-patient AZURE_QUEUE_APP=swiftcare-queue
export AZURE_MEDICAL_RECORD_APP=swiftcare-medical-record AZURE_PRESCRIPTION_APP=swiftcare-prescription
export AZURE_NOTIFICATION_APP=swiftcare-notification
export AZURE_GATEWAY_APP=swiftcare-gateway AUTH_BOOTSTRAP_JOB=swiftcare-auth-bootstrap
export INITIAL_ADMIN_USERNAME=dummy-admin INITIAL_ADMIN_PASSWORD=dummy-dummy

# jq.exe on Windows ends lines with CRLF.
jq() { command jq "$@" | tr -d $'\r'; }

passed=0
failed=0

reset() {
  rm -rf "$FAKE_AZ_STATE"
  mkdir -p "$FAKE_AZ_STATE"
  : > "$FAKE_AZ_LOG"
  : > "$GITHUB_OUTPUT"
  unset FAKE_AZ_UNREADY_IMAGE FAKE_MIGRATION_STATUS FAKE_CURL_BODY FAKE_AZ_FLAKY_ONCE
}

# add_app NAME TAG STATE PORT
add_app() {
  local name="$1" tag="$2" running="$3" port="$4" image
  image="ghcr.io/org/${name}:${tag}"
  echo "$image" > "$FAKE_AZ_STATE/$name.image"
  echo "$running" > "$FAKE_AZ_STATE/$name.running"
  echo "$name--1" > "$FAKE_AZ_STATE/$name.rev"
  echo "$name--1" > "$FAKE_AZ_STATE/$name.ready"
  echo "$port" > "$FAKE_AZ_STATE/$name.port"
}

ok() {
  passed=$((passed + 1))
  printf 'PASS  %s\n' "$1"
}

bad() {
  failed=$((failed + 1))
  printf 'FAIL  %s\n' "$1"
  if [ -n "${2:-}" ]; then
    while IFS= read -r line; do printf '        %s\n' "$line"; done <<<"$2"
  fi
}

# expect_success DESC COMMAND... / expect_failure DESC COMMAND...
expect_success() {
  local description="$1"
  shift
  if out=$("$@" 2>&1); then ok "$description"; else bad "$description" "$out"; fi
}

expect_failure() {
  local description="$1"
  shift
  if out=$("$@" 2>&1); then bad "$description (expected a failure)" "$out"; else ok "$description"; fi
}

expect_equal() { # DESC ACTUAL EXPECTED
  if [ "$2" = "$3" ]; then ok "$1"; else bad "$1" "expected: $3"$'\n'"actual:   $2"; fi
}

log_has() { grep -qF -- "$1" "$FAKE_AZ_LOG"; }

expect_logged() { if log_has "$2"; then ok "$1"; else bad "$1" "not logged: $2"; fi; }
expect_not_logged() { if log_has "$2"; then bad "$1" "unexpectedly logged: $2"; else ok "$1"; fi; }

image_of() { cat "$FAKE_AZ_STATE/$1.image"; }

echo "== capture-previous"
reset
add_app swiftcare-queue "$OLD" Stopped 5003
expect_success "captures an existing app" bash "$HERE/capture-previous.sh" queue
expect_equal "records the running image" "$(grep '^previous_image=' "$GITHUB_OUTPUT")" "previous_image=ghcr.io/org/swiftcare-queue:$OLD"
expect_equal "records the running state" "$(grep '^previous_state=' "$GITHUB_OUTPUT")" "previous_state=Stopped"
: > "$GITHUB_OUTPUT"
expect_success "accepts an app that does not exist yet" bash "$HERE/capture-previous.sh" prescription
expect_equal "records an empty image for a first deployment" "$(grep '^previous_image=' "$GITHUB_OUTPUT")" "previous_image="

echo "== migrate-service"
reset
export SERVICE_MIGRATE_JOB=swiftcare-patient-migrate
expect_success "creates and runs a missing migration job" bash "$HERE/migrate-service.sh" patient
expect_logged "creates the job with the migrate argument" "--args=--migrate"
expect_logged "creates the job for the patient database image" "ghcr.io/org/swiftcare-patient:$NEW"
expect_logged "starts the job" "containerapp job start --resource-group rg --name swiftcare-patient-migrate"
expect_not_logged "touches no other migration job" "swiftcare-queue-migrate"
: > "$FAKE_AZ_LOG"
expect_success "reuses an existing migration job" bash "$HERE/migrate-service.sh" patient
expect_logged "updates the existing job" "containerapp job update --resource-group rg --name swiftcare-patient-migrate"
export FAKE_MIGRATION_STATUS=Failed
expect_failure "fails when the migration execution fails" bash "$HERE/migrate-service.sh" patient
unset FAKE_MIGRATION_STATUS

echo "== deploy-service"
reset
add_app swiftcare-queue "$OLD" Running 5003
add_app swiftcare-auth "$OLD" Running 5000
expect_success "updates a running service" bash "$HERE/deploy-service.sh" queue
expect_equal "runs the new image" "$(image_of swiftcare-queue)" "ghcr.io/org/swiftcare-queue:$NEW"
expect_equal "leaves other apps on their image" "$(image_of swiftcare-auth)" "ghcr.io/org/swiftcare-auth:$OLD"
expect_not_logged "does not touch other apps" "--name swiftcare-auth"
expect_logged "passes the queue topic setting" "Kafka__PatientCheckedInTopic=patient-checked-in"
expect_logged "passes the listen url from the manifest port" "ASPNETCORE_URLS=http://+:5003"
expect_not_logged "does not give the queue JWT settings" "Jwt__SecretKey"
: > "$FAKE_AZ_LOG"
expect_success "updates AuthService with JWT settings" bash "$HERE/deploy-service.sh" auth
expect_logged "passes the JWT secret reference" "Jwt__SecretKey=secretref:jwt-key"

reset
add_app swiftcare-notification "$OLD" Running 5005
add_app swiftcare-queue "$OLD" Running 5003
expect_success "deploys NotificationService on its own" bash "$HERE/deploy-service.sh" notification
expect_equal "runs the new notification image" "$(image_of swiftcare-notification)" "ghcr.io/org/swiftcare-notification:$NEW"
expect_equal "leaves the queue image alone" "$(image_of swiftcare-queue)" "ghcr.io/org/swiftcare-queue:$OLD"
expect_logged "listens on its own port" "ASPNETCORE_URLS=http://+:5005"
expect_logged "passes its scoped connection setting" "ConnectionStrings__NotificationDb=secretref:notification-db"
expect_logged "passes the Kafka server" "Kafka__BootstrapServers=kafka.example.test:9092"
expect_not_logged "does not give it JWT settings" "Jwt__SecretKey"
expect_not_logged "does not touch the queue app" "--name swiftcare-queue"
export SERVICE_MIGRATE_JOB=swiftcare-notification-migrate
expect_success "migrates only the notification database" bash "$HERE/migrate-service.sh" notification
expect_logged "runs the notification migration job" "containerapp job start --resource-group rg --name swiftcare-notification-migrate"
export SERVICE_MIGRATE_JOB=swiftcare-patient-migrate

reset
add_app swiftcare-prescription "$OLD" Stopped 5001
expect_success "starts a stopped service" bash "$HERE/deploy-service.sh" prescription
expect_logged "requests a start" "/start?api-version="
expect_equal "ends Running" "$(cat "$FAKE_AZ_STATE/swiftcare-prescription.running")" "Running"

reset
expect_success "creates a service that does not exist" bash "$HERE/deploy-service.sh" prescription
expect_logged "creates it with internal ingress on its port" "--ingress internal --target-port 5001"

reset
add_app swiftcare-patient "$OLD" Running 5002
export FAKE_AZ_UNREADY_IMAGE="ghcr.io/org/swiftcare-patient:$NEW"
expect_failure "fails when the new revision never becomes ready" bash "$HERE/deploy-service.sh" patient
unset FAKE_AZ_UNREADY_IMAGE

reset
add_app swiftcare-queue "$OLD" Running 5003
export FAKE_AZ_FLAKY_ONCE=1
expect_success "survives one transient readiness read failure" bash "$HERE/deploy-service.sh" queue
expect_equal "still ends on the new image" "$(image_of swiftcare-queue)" "ghcr.io/org/swiftcare-queue:$NEW"
unset FAKE_AZ_FLAKY_ONCE

reset
unset JWT_ISSUER
expect_failure "names a missing configuration value" bash "$HERE/deploy-service.sh" auth
if [ "$(bash "$HERE/deploy-service.sh" auth 2>&1 | grep -c 'JWT_ISSUER')" -ge 1 ]; then ok "reports the missing name"; else bad "reports the missing name"; fi
export JWT_ISSUER=issuer

echo "== two-service release where one service fails"
reset
add_app swiftcare-queue "$OLD" Running 5003
add_app swiftcare-patient "$OLD" Running 5002
export FAKE_AZ_UNREADY_IMAGE="ghcr.io/org/swiftcare-patient:$NEW"
bash "$HERE/deploy-service.sh" queue > /dev/null 2>&1; queue_status=$?
bash "$HERE/deploy-service.sh" patient > /dev/null 2>&1; patient_status=$?
expect_equal "the healthy service deploys" "$queue_status" "0"
expect_equal "the failing service fails" "$([ "$patient_status" -ne 0 ] && echo failed)" "failed"
unset FAKE_AZ_UNREADY_IMAGE
expect_success "rolls back only the failing service" bash "$HERE/rollback-service.sh" patient "ghcr.io/org/swiftcare-patient:$OLD"
expect_equal "the failing service is back on its previous image" "$(image_of swiftcare-patient)" "ghcr.io/org/swiftcare-patient:$OLD"
expect_equal "the healthy service keeps the new image" "$(image_of swiftcare-queue)" "ghcr.io/org/swiftcare-queue:$NEW"
expect_equal "rollback reports done" "$(grep '^rollback=' "$GITHUB_OUTPUT")" "rollback=done"

echo "== rollback-service"
reset
add_app swiftcare-patient "$NEW" Running 5002
expect_success "accepts a first deployment with no previous image" bash "$HERE/rollback-service.sh" patient ""
expect_equal "reports unavailable" "$(grep '^rollback=' "$GITHUB_OUTPUT")" "rollback=unavailable"
expect_not_logged "changes nothing" "containerapp update"
reset
add_app swiftcare-patient "$OLD" Running 5002
: > "$GITHUB_OUTPUT"
expect_success "accepts a release that did not change the image" bash "$HERE/rollback-service.sh" patient "ghcr.io/org/swiftcare-patient:$OLD"
expect_equal "reports that the image was the same" "$(grep '^rollback=' "$GITHUB_OUTPUT")" "rollback=same"
expect_not_logged "does not redeploy the same image" "containerapp update"

echo "== deploy-gateway"
reset
for entry in "swiftcare-auth:5000" "swiftcare-patient:5002" "swiftcare-queue:5003" \
  "swiftcare-medical-record:5004" "swiftcare-prescription:5001" "swiftcare-notification:5005"; do
  add_app "${entry%%:*}" "$OLD" Running "${entry##*:}"
done
add_app swiftcare-prescription "$OLD" Stopped 5001
add_app swiftcare-gateway "$OLD" Running 8000
expect_success "deploys the gateway" bash "$HERE/deploy-gateway.sh"
expect_equal "runs the new gateway image" "$(image_of swiftcare-gateway)" "ghcr.io/org/swiftcare-gateway:$NEW"
expect_equal "starts a stopped service the gateway routes to" "$(cat "$FAKE_AZ_STATE/swiftcare-prescription.running")" "Running"
expect_equal "leaves service images alone" "$(image_of swiftcare-auth)" "ghcr.io/org/swiftcare-auth:$OLD"
for cluster in auth patient queue medical-record prescription notification; do
  expect_logged "routes the $cluster cluster to its internal address" \
    "ReverseProxy__Clusters__${cluster}-cluster__Destinations__${cluster}-destination__Address=https://swiftcare-${cluster}.internal.example.test"
done
expect_equal "publishes the gateway url" "$(grep '^url=' "$GITHUB_OUTPUT")" "url=https://swiftcare-gateway.internal.example.test"

rm -f "$FAKE_AZ_STATE"/swiftcare-medical-record.*
: > "$FAKE_AZ_LOG"
expect_success "deploys without an app that does not exist" bash "$HERE/deploy-gateway.sh"
expect_not_logged "adds no address for the missing app" "medical-record-destination"

reset
for entry in "swiftcare-auth:5000" "swiftcare-patient:5002" "swiftcare-queue:5003" \
  "swiftcare-medical-record:5004" "swiftcare-prescription:5001" "swiftcare-notification:5005"; do
  add_app "${entry%%:*}" "$OLD" Running "${entry##*:}"
done
expect_success "creates a gateway that does not exist" bash "$HERE/deploy-gateway.sh"
expect_logged "creates it with external ingress on 8000" "--ingress external --target-port 8000"

echo "== bootstrap-admin"
reset
export SERVICE_DB_USER=dbuser
expect_success "creates, runs and removes the bootstrap job" bash "$HERE/bootstrap-admin.sh"
expect_equal "no bootstrap job remains" "$([ -f "$FAKE_AZ_STATE/job.swiftcare-auth-bootstrap" ] && echo present || echo removed)" "removed"
export FAKE_MIGRATION_STATUS=Failed
expect_failure "fails when the bootstrap execution fails" bash "$HERE/bootstrap-admin.sh"
expect_equal "removes the job even on failure" "$([ -f "$FAKE_AZ_STATE/job.swiftcare-auth-bootstrap" ] && echo present || echo removed)" "removed"
unset FAKE_MIGRATION_STATUS

echo "== read-state"
reset
add_app swiftcare-auth "$OLD" Stopped 5000
add_app swiftcare-queue "not-a-commit" Running 5003
export FAKE_CURL_BODY="{\"sha\":\"$NEW\"}"
expect_success "reads the apps and the frontend commit" bash "$HERE/read-state.sh" "$WORK/state.json"
expect_equal "records an app image" "$(jq -r '.apps.auth.image' "$WORK/state.json")" "ghcr.io/org/swiftcare-auth:$OLD"
expect_equal "records a missing app" "$(jq -r '.apps.patient.exists' "$WORK/state.json")" "false"
expect_equal "records a ready latest revision" "$(jq -r '.apps.auth.ready' "$WORK/state.json")" "true"
echo "swiftcare-queue--broken" > "$FAKE_AZ_STATE/swiftcare-queue.rev"
bash "$HERE/read-state.sh" "$WORK/state.json" > /dev/null 2>&1
expect_equal "records a latest revision that never became ready" "$(jq -r '.apps.queue.ready' "$WORK/state.json")" "false"
expect_equal "records the frontend commit" "$(jq -r '.frontend.sha' "$WORK/state.json")" "$NEW"
export FAKE_CURL_BODY='<html>not json</html>'
bash "$HERE/read-state.sh" "$WORK/state.json" > /dev/null 2>&1
expect_equal "treats a page that is not version json as unknown" "$(jq -r '.frontend.sha' "$WORK/state.json")" "null"
unset FAKE_CURL_BODY
bash "$HERE/read-state.sh" "$WORK/state.json" > /dev/null 2>&1
expect_equal "treats an unreachable frontend as unknown" "$(jq -r '.frontend.sha' "$WORK/state.json")" "null"
expect_success "feeds selection with the real script" python3 -I "$HERE/select_services.py" \
  --manifest "$HERE/services.json" --state "$WORK/state.json" --release-sha "$NEW" --mode queue

echo "== smoke"
export STUB_LOG="$WORK/stub.log"
start_stub() { # start_stub : launch the stub gateway and set GATEWAY_URL
  : > "$STUB_LOG"
  rm -f "$WORK/stub.port"
  python3 -I "$HERE/testdata/stub_gateway.py" "$WORK/stub.port" &
  STUB_PID=$!
  for _ in $(seq 1 50); do [ -s "$WORK/stub.port" ] && break; sleep 0.1; done
  local port
  port=$(cat "$WORK/stub.port")
  export GATEWAY_URL="http://127.0.0.1:$port"
}
stop_stub() { kill "$STUB_PID" 2>/dev/null; wait "$STUB_PID" 2>/dev/null; }
export CD_SMOKE_ATTEMPTS=2 CD_SMOKE_INTERVAL=0
export SMOKE_RECEPTIONIST_USERNAME=receptionist-smoke SMOKE_RECEPTIONIST_PASSWORD=pw-r
export SMOKE_DOCTOR_USERNAME=doctor-smoke SMOKE_DOCTOR_PASSWORD=pw-d

start_stub
for target in auth patient queue medical-record prescription notification gateway; do
  expect_success "smoke $target passes against a healthy gateway" bash "$HERE/smoke.sh" "$target"
done
: > "$STUB_LOG"
bash "$HERE/smoke.sh" patient > /dev/null 2>&1
expect_equal "revokes the token it created" "$(grep -c 'POST /api/auth/logout' "$STUB_LOG")" "1"
expect_equal "does not sign in a role the target does not need" "$(grep -c 'POST /api/auth/login' "$STUB_LOG")" "1"
# The runner consumes ::add-mask:: commands and does not print them, so they are not output.
out=$(bash "$HERE/smoke.sh" gateway 2>&1 | grep -v '^::add-mask::')
if grep -q 'tok-r\|tok-d\|pw-r\|pw-d' <<<"$out"; then bad "smoke output hides tokens and passwords" "$out"; else ok "smoke output hides tokens and passwords"; fi
stop_stub

export STUB_BROKEN="GET /api/notifications/today"
start_stub
expect_failure "smoke notification fails when the feed route answers 500" bash "$HERE/smoke.sh" notification
expect_success "smoke patient still passes when only notification is broken" bash "$HERE/smoke.sh" patient
expect_failure "smoke gateway fails when the notification route is broken" bash "$HERE/smoke.sh" gateway
stop_stub

export STUB_BROKEN="GET /api/patients/search"
start_stub
expect_failure "smoke patient fails when the route answers 500" bash "$HERE/smoke.sh" patient
expect_success "smoke queue still passes when only patient is broken" bash "$HERE/smoke.sh" queue
expect_failure "smoke gateway fails when any service route is broken" bash "$HERE/smoke.sh" gateway
stop_stub
unset STUB_BROKEN

export STUB_BAD_DISPLAY=1
start_stub
expect_failure "smoke queue fails on a wrong display shape" bash "$HERE/smoke.sh" queue
stop_stub
unset STUB_BAD_DISPLAY

start_stub
SMOKE_RECEPTIONIST_PASSWORD=wrong expect_failure "smoke fails when the account cannot sign in" bash "$HERE/smoke.sh" prescription
: > "$STUB_LOG"
SMOKE_FORCE_FAIL=prescription expect_failure "forced failure fails the chosen target" bash "$HERE/smoke.sh" prescription
expect_equal "forced failure makes no request" "$(wc -l < "$STUB_LOG" | tr -d ' ')" "0"
SMOKE_FORCE_FAIL=prescription expect_success "forced failure leaves other targets alone" bash "$HERE/smoke.sh" patient
(unset SMOKE_DOCTOR_PASSWORD; expect_failure "names a missing smoke secret" bash "$HERE/smoke.sh" medical-record)
expect_failure "rejects an unknown target" bash "$HERE/smoke.sh" billing
stop_stub

echo "== prepare-smoke"
reset
add_app swiftcare-auth "$OLD" Stopped 5000
add_app swiftcare-gateway "$OLD" Stopped 8000
expect_success "starts the stopped gateway and auth" bash "$HERE/prepare-smoke.sh" queue
expect_equal "both are running" "$(cat "$FAKE_AZ_STATE/swiftcare-auth.running")/$(cat "$FAKE_AZ_STATE/swiftcare-gateway.running")" "Running/Running"
expect_equal "publishes the gateway url" "$(grep '^gateway_url=' "$GITHUB_OUTPUT")" "gateway_url=https://swiftcare-gateway.internal.example.test"
expect_equal "does not change images" "$(image_of swiftcare-gateway)" "ghcr.io/org/swiftcare-gateway:$OLD"
reset
expect_success "skips smoke when the gateway was never deployed" bash "$HERE/prepare-smoke.sh" queue
expect_equal "reports skip" "$(grep '^skip=' "$GITHUB_OUTPUT")" "skip=true"
reset
add_app swiftcare-gateway "$OLD" Running 8000
expect_failure "fails when AuthService is not deployed" bash "$HERE/prepare-smoke.sh" queue
expect_success "does not require AuthService when releasing AuthService" bash "$HERE/prepare-smoke.sh" auth
reset
add_app swiftcare-auth "$OLD" Running 5000
add_app swiftcare-gateway "$OLD" Running 8000
printf 'ReverseProxy__Clusters__auth-cluster__Destinations__auth-destination__Address\n' > "$FAKE_AZ_STATE/swiftcare-gateway.envnames"
expect_success "skips smoke for a service the running gateway has no address for" bash "$HERE/prepare-smoke.sh" notification
expect_equal "reports skip for a first deployment" "$(grep '^skip=' "$GITHUB_OUTPUT")" "skip=true"
: > "$GITHUB_OUTPUT"
expect_success "still prepares a service the gateway does know" bash "$HERE/prepare-smoke.sh" auth
expect_equal "reports no skip for a known service" "$(grep '^skip=' "$GITHUB_OUTPUT")" "skip=false"

echo "== validate-config"
export AZURE_LOCATION=eastasia AZURE_CLIENT_ID=client AZURE_TENANT_ID=tenant AZURE_SUBSCRIPTION_ID=subscription
export STATIC_WEB_APP_NAME=site GATEWAY_ORIGIN=https://api.example.test AZURE_STATIC_WEB_APPS_API_TOKEN=token
expect_success "accepts complete plan configuration" bash "$HERE/validate-config.sh" plan
expect_success "accepts complete service configuration" bash "$HERE/validate-config.sh" service queue
expect_success "accepts complete gateway configuration" bash "$HERE/validate-config.sh" gateway
expect_success "accepts complete frontend configuration" bash "$HERE/validate-config.sh" frontend
out=$(unset KAFKA_QUEUE_CONSUMER_GROUP; bash "$HERE/validate-config.sh" service queue 2>&1)
if grep -q 'KAFKA_QUEUE_CONSUMER_GROUP' <<<"$out"; then ok "names the missing queue value"; else bad "names the missing queue value" "$out"; fi
out=$(unset SMOKE_DOCTOR_PASSWORD; bash "$HERE/validate-config.sh" service medical-record 2>&1)
if grep -q 'SMOKE_DOCTOR_PASSWORD' <<<"$out"; then ok "requires the smoke account the service uses"; else bad "requires the smoke account the service uses" "$out"; fi
(unset SMOKE_DOCTOR_PASSWORD; expect_success "does not require a smoke account the service does not use" bash "$HERE/validate-config.sh" service queue)
(JWT_SIGNING_KEY=short expect_failure "rejects a short signing key" bash "$HERE/validate-config.sh" gateway)
long_name=swiftcare-prod-prescription-migrate
(SERVICE_MIGRATE_JOB=$long_name expect_failure "rejects a migration job name longer than 32 characters" bash "$HERE/validate-config.sh" service queue)
(SERVICE_MIGRATE_JOB=swc-prod-medical-record-migrate expect_success "accepts a 31 character job name" bash "$HERE/validate-config.sh" service queue)
(SERVICE_MIGRATE_JOB=Swiftcare-Queue-Migrate expect_failure "rejects upper case in a job name" bash "$HERE/validate-config.sh" service queue)
(SERVICE_MIGRATE_JOB=swiftcare--queue expect_failure "rejects two hyphens in a row in a job name" bash "$HERE/validate-config.sh" service queue)
(AZURE_QUEUE_APP=swiftcare-queue- expect_failure "rejects an app name ending in a hyphen" bash "$HERE/validate-config.sh" service queue)
(AZURE_GATEWAY_APP=swiftcare-production-gateway-app-name expect_failure "rejects an app name longer than 32 characters in the plan" bash "$HERE/validate-config.sh" plan)
(FRONTEND_WWW_ORIGIN="$FRONTEND_ORIGIN" expect_failure "rejects identical frontend origins" bash "$HERE/validate-config.sh" gateway)
(FRONTEND_ORIGIN=https://app.example.test/ expect_failure "rejects an origin with a trailing slash" bash "$HERE/validate-config.sh" gateway)
(GATEWAY_ORIGIN=http://api.example.test expect_failure "rejects a non-HTTPS gateway origin" bash "$HERE/validate-config.sh" frontend)
(unset AZURE_PRESCRIPTION_APP; expect_failure "plan requires every app name" bash "$HERE/validate-config.sh" plan)
expect_failure "rejects an unknown mode" bash "$HERE/validate-config.sh" everything

echo "== verify-target azure"
verify_azure() { # AZURE_LOCATION ENV_REGION SWA_REGION [STATIC_WEB_APP_LOCATION]
  AZURE_LOCATION="$1" FAKE_ENV_LOCATION="$2" FAKE_SWA_LOCATION="$3" STATIC_WEB_APP_LOCATION="${4:-}" \
    AZURE_SUBSCRIPTION_ID=sub-test STATIC_WEB_APP_NAME=site bash "$HERE/verify-target.sh" azure
}
reset
expect_success "accepts one region for everything" verify_azure eastasia "East Asia" "East Asia"
expect_failure "rejects a site in another region by default" verify_azure koreacentral "Korea Central" "East Asia"
expect_success "accepts the site region named by STATIC_WEB_APP_LOCATION" verify_azure koreacentral "Korea Central" "East Asia" eastasia
expect_failure "still rejects an environment outside AZURE_LOCATION" verify_azure koreacentral "East Asia" "East Asia" eastasia
expect_failure "rejects a site outside the named region" verify_azure koreacentral "Korea Central" "West Europe" eastasia

echo "== shared Container Apps environment"
reset
expect_success "creates a first service app in its own group" bash "$HERE/deploy-service.sh" queue
expect_logged "names the environment directly when it is in the same group" "--environment env "
export CONTAINERAPPS_ENVIRONMENT_RESOURCE_GROUP=shared-rg
reset
expect_success "creates a service app that joins an environment in another group" bash "$HERE/deploy-service.sh" queue
expect_logged "looks the environment up in its own group" "containerapp env show --resource-group shared-rg --name env"
expect_logged "passes the environment resource ID" "--environment /subscriptions/test/resourceGroups/shared-rg/providers/Microsoft.App/managedEnvironments/env"
expect_logged "keeps the app in its own resource group" "containerapp create --resource-group rg --name swiftcare-queue"
export SERVICE_MIGRATE_JOB=swiftcare-queue-migrate
expect_success "creates a migration job in the shared environment" bash "$HERE/migrate-service.sh" queue
expect_logged "passes the environment resource ID to the job" "job create --resource-group rg --name swiftcare-queue-migrate --environment /subscriptions/test/resourceGroups/shared-rg/"
expect_success "checks the shared environment's region in its own group" \
  env AZURE_LOCATION=eastasia AZURE_SUBSCRIPTION_ID=sub-test STATIC_WEB_APP_NAME=site bash "$HERE/verify-target.sh" azure
unset CONTAINERAPPS_ENVIRONMENT_RESOURCE_GROUP

echo "== resolve-target"
# A fixed clock: 20 October 2026 10:00 UTC. Windows end at 18:00 the same day unless a test says otherwise.
WINDOW_NOW=$(date -u -d 2026-10-20T10:00Z +%s)
export WINDOW_NOW
UNTIL_DEVELOPMENT=2026-10-20T18:00Z
UNTIL_PRODUCTION=2026-10-20T18:00Z
target_of() { # EVENT RUN_BRANCH REF_NAME TARGET_INPUT WINDOW_DEVELOPMENT WINDOW_PRODUCTION
  : > "$GITHUB_OUTPUT"
  if ! EVENT_NAME="$1" RUN_BRANCH="$2" REF_NAME="$3" TARGET_INPUT="$4" WINDOW_DEVELOPMENT="$5" \
    WINDOW_PRODUCTION="$6" WINDOW_DEVELOPMENT_UNTIL="$UNTIL_DEVELOPMENT" \
    WINDOW_PRODUCTION_UNTIL="$UNTIL_PRODUCTION" GITHUB_STEP_SUMMARY="" \
    bash "$HERE/resolve-target.sh" > /dev/null 2>&1; then
    echo failed
    return
  fi
  printf '%s %s' "$(grep '^environment=' "$GITHUB_OUTPUT" | cut -d= -f2)" "$(grep '^proceed=' "$GITHUB_OUTPUT" | cut -d= -f2)"
}
expect_equal "CI on develop releases to development in an open window" "$(target_of workflow_run develop develop '' true false)" "azure-development true"
expect_equal "CI on main releases to production in an open window" "$(target_of workflow_run main main '' false true)" "azure-production true"
expect_equal "a closed development window stops the run" "$(target_of workflow_run develop develop '' false true)" "azure-development false"
expect_equal "a closed production window stops the run" "$(target_of workflow_run main main '' true false)" "azure-production false"
expect_equal "an unset window counts as closed" "$(target_of workflow_run main main '' '' '')" "azure-production false"
expect_equal "only the exact value true opens a window" "$(target_of workflow_run develop develop '' TRUE false)" "azure-development false"
expect_equal "a manual run defaults to development" "$(target_of workflow_dispatch '' feature/x '' true false)" "azure-development true"
expect_equal "a manual production run from main is allowed" "$(target_of workflow_dispatch '' main production false true)" "azure-production true"
expect_equal "a manual production run from another branch fails" "$(target_of workflow_dispatch '' develop production true true)" "failed"
expect_equal "an unknown manual target fails" "$(target_of workflow_dispatch '' main staging true true)" "failed"
expect_equal "CI on another branch fails" "$(target_of workflow_run feature/x feature/x '' true true)" "failed"
UNTIL_PRODUCTION=""
expect_equal "an open window without an end time counts as closed" "$(target_of workflow_run main main '' false true)" "azure-production false"
UNTIL_PRODUCTION=2026-10-20T09:59Z
expect_equal "an open window whose end time has passed counts as closed" "$(target_of workflow_run main main '' false true)" "azure-production false"
UNTIL_PRODUCTION=2026-10-21T10:01Z
expect_equal "an end time more than 24 hours away counts as closed" "$(target_of workflow_run main main '' false true)" "azure-production false"
UNTIL_PRODUCTION=2026-10-20T18:00
expect_equal "an end time without the UTC marker counts as closed" "$(target_of workflow_run main main '' false true)" "azure-production false"
UNTIL_PRODUCTION=2026-10-20T18:00:00Z
expect_equal "an end time with seconds is accepted" "$(target_of workflow_run main main '' false true)" "azure-production true"
UNTIL_DEVELOPMENT=2026-10-20T09:00Z
expect_equal "each environment uses its own end time" "$(target_of workflow_run main main '' true true)" "azure-production true"
UNTIL_DEVELOPMENT=2026-10-20T18:00Z
UNTIL_PRODUCTION=2026-10-20T18:00Z

echo "== verify-target data (Kafka Container App)"
verify_data() {
  env AZURE_LOCATION=eastasia MYSQL_SERVER_NAME=mysql KAFKA_APP=swiftcare-prod-kafka \
    KAFKA_BOOTSTRAP_SERVERS=swiftcare-prod-kafka:9092 "$@" bash "$HERE/verify-target.sh" data
}
reset
add_app swiftcare-prod-kafka "$OLD" Running 9092
expect_success "accepts a running internal TCP Kafka app" verify_data
expect_not_logged "does not look for a container group" "container show"
expect_failure "rejects a bootstrap address that is not the Kafka app" verify_data KAFKA_BOOTSTRAP_SERVERS=kafka.swiftcare.internal:9092
expect_failure "rejects external Kafka ingress" verify_data FAKE_KAFKA_EXTERNAL=true
expect_failure "rejects HTTP Kafka ingress" verify_data FAKE_KAFKA_TRANSPORT=Http
expect_failure "rejects a Kafka app in another region" verify_data AZURE_LOCATION=southeastasia
echo Stopped > "$FAKE_AZ_STATE/swiftcare-prod-kafka.running"
expect_failure "rejects a stopped Kafka app" verify_data
out=$(verify_data AZURE_LOCATION=eastasia 2>&1 || true)
if grep -q 'az containerapp start' <<<"$out"; then ok "says how to start the Kafka app"; else bad "says how to start the Kafka app" "$out"; fi
reset
expect_failure "rejects a missing Kafka app" verify_data
add_app swiftcare-prod-kafka "$OLD" Running 9092
expect_failure "rejects MySQL that is not Ready" verify_data FAKE_MYSQL_STATE=Stopped
expect_failure "requires the container group names when KAFKA_APP is empty" verify_data KAFKA_APP=

echo "== window-guard"
guard_decide() { # WINDOW UNTIL ACTIVE_CD_RUNS
  : > "$GITHUB_OUTPUT"
  WINDOW="$1" WINDOW_UNTIL="$2" ACTIVE_CD_RUNS="$3" GITHUB_STEP_SUMMARY="" \
    bash "$HERE/window-guard.sh" decide > /dev/null 2>&1 || { echo failed; return; }
  grep '^act=' "$GITHUB_OUTPUT" | cut -d= -f2
}
expect_equal "keeps an open window running" "$(guard_decide true 2026-10-20T18:00Z 0)" "false"
expect_equal "stops a closed window" "$(guard_decide false 2026-10-20T18:00Z 0)" "true"
expect_equal "stops an expired window" "$(guard_decide true 2026-10-20T09:00Z 0)" "true"
expect_equal "stops an open window with no end time" "$(guard_decide true '' 0)" "true"
expect_equal "waits while a CD run is active" "$(guard_decide false '' 1)" "false"

guard() {
  env RESOURCE_GROUP=rg MYSQL_SERVER_NAME=mysql KAFKA_APP=swiftcare-prod-kafka GITHUB_STEP_SUMMARY="" \
    "$@" bash "$HERE/window-guard.sh" enforce
}
reset
add_app swiftcare-prod-gateway "$OLD" Running 8000
add_app swiftcare-prod-kafka "$OLD" Running 9092
add_app swiftcare-prod-queue "$OLD" Running 5003
add_app swiftcare-prod-auth "$OLD" Stopped 5000
expect_success "a dry run succeeds" guard DRY_RUN=true
expect_not_logged "a dry run stops no app" "/stop?"
expect_not_logged "a dry run leaves MySQL running" "flexible-server stop"
: > "$FAKE_AZ_LOG"
expect_success "stops every running app, Kafka and MySQL" guard
expect_logged "stops the gateway through the management API" "apps/swiftcare-prod-gateway/stop?api-version="
expect_logged "stops the queue service" "apps/swiftcare-prod-queue/stop?api-version="
expect_logged "stops the Kafka app" "apps/swiftcare-prod-kafka/stop?api-version="
expect_not_logged "never calls the missing containerapp stop command" "containerapp stop"
expect_not_logged "leaves an already stopped app alone" "--name swiftcare-prod-auth"
expect_logged "stops MySQL" "mysql flexible-server stop --resource-group rg --name mysql"
expect_equal "stops Kafka after the apps" \
  "$(grep '/stop?' "$FAKE_AZ_LOG" | tail -1 | grep -c swiftcare-prod-kafka)" "1"
expect_not_logged "never starts an app" "containerapp start"
expect_not_logged "never deletes anything" "delete"
: > "$FAKE_AZ_LOG"
expect_success "a second run finds nothing to stop" guard
expect_not_logged "stops no app the second time" "/stop?"
expect_not_logged "stops no server the second time" " stop "
reset
add_app swiftcare-prod-gateway "$OLD" Running 8000
add_app swiftcare-prod-queue "$OLD" Running 5003
expect_failure "reports a resource it could not stop" guard FAKE_STOP_FAILS=swiftcare-prod-gateway
expect_logged "still stops the other apps after a failure" "apps/swiftcare-prod-queue/stop?api-version="
reset
expect_success "stops a running legacy container group" guard KAFKA_APP= KAFKA_CONTAINER_GROUP=group
expect_logged "stops the container group" "container stop --resource-group rg --name group"

guard_cost() {
  env AZURE_SUBSCRIPTION_ID=sub-test COST_SINCE=2026-10-18 GITHUB_STEP_SUMMARY="" CD_WAIT_INTERVAL=0 \
    "$@" bash "$HERE/window-guard.sh" cost
}
reset
out=$(guard_cost 2>&1)
if grep -q 'USD 12.35' <<<"$out"; then ok "reports the spend rounded to cents"; else bad "reports the spend rounded to cents" "$out"; fi
out=$(guard_cost COST_WARN_USD=10 2>&1)
if grep -q '::warning::' <<<"$out"; then ok "warns above the threshold"; else bad "warns above the threshold" "$out"; fi
out=$(guard_cost COST_WARN_USD=60 2>&1)
if grep -q '::warning::' <<<"$out"; then bad "stays quiet below the threshold" "$out"; else ok "stays quiet below the threshold"; fi
expect_success "a Cost Management failure does not fail the run" guard_cost FAKE_COST_FAILS=1
expect_success "skips the report without a start date" guard_cost COST_SINCE=
expect_failure "rejects a malformed start date" guard_cost COST_SINCE=18-10-2026

echo "== frontend on a Container App"
(FRONTEND_HOST=container_app AZURE_FRONTEND_APP=swiftcare-prod-frontend STATIC_WEB_APP_NAME='' \
  expect_success "plan accepts a Container App frontend without a Static Web App" bash "$HERE/validate-config.sh" plan)
(FRONTEND_HOST=container_app AZURE_FRONTEND_APP='' \
  expect_failure "plan requires the frontend app name for a Container App frontend" bash "$HERE/validate-config.sh" plan)
(STATIC_WEB_APP_NAME='' expect_failure "plan still requires the site for Static Web Apps" bash "$HERE/validate-config.sh" plan)
(FRONTEND_HOST=bucket expect_failure "rejects an unknown frontend host" bash "$HERE/validate-config.sh" plan)
(FRONTEND_HOST=container_app AZURE_FRONTEND_APP=swiftcare-prod-frontend TARGET=production AZURE_STATIC_WEB_APPS_API_TOKEN='' \
  expect_success "frontend release needs no site token on a Container App" bash "$HERE/validate-config.sh" frontend)
(FRONTEND_HOST=container_app AZURE_FRONTEND_APP=swiftcare-prod-frontend TARGET=development \
  expect_failure "refuses a Container App frontend for development (no dev_team policy)" bash "$HERE/validate-config.sh" frontend)
(FRONTEND_HOST=container_app AZURE_FRONTEND_APP=swiftcare-prod-frontend TARGET=production GHCR_TOKEN='' \
  expect_failure "frontend release on a Container App needs the registry credential" bash "$HERE/validate-config.sh" frontend)
(AZURE_STATIC_WEB_APPS_API_TOKEN='' expect_failure "frontend release on Static Web Apps still needs its token" bash "$HERE/validate-config.sh" frontend)
reset
expect_success "verify-target skips the site for a Container App frontend" \
  env FRONTEND_HOST=container_app STATIC_WEB_APP_NAME='' AZURE_LOCATION=eastasia AZURE_SUBSCRIPTION_ID=sub-test \
  bash "$HERE/verify-target.sh" azure
expect_not_logged "does not look for a Static Web App" "staticwebapp show"

deploy_frontend() { env AZURE_FRONTEND_APP=swiftcare-prod-frontend FAKE_CURL_BODY="{\"sha\":\"$NEW\"}" "$@" bash "$HERE/deploy-frontend.sh"; }
reset
expect_success "creates the frontend app on first release" deploy_frontend
expect_logged "creates it with public ingress on 8080" "containerapp create --resource-group rg --name swiftcare-prod-frontend"
expect_logged "uses the release image" "ghcr.io/org/swiftcare-frontend:$NEW"
expect_logged "lets it scale to zero" "--min-replicas 0 --max-replicas 2"
expect_logged "scales on HTTP requests" "--scale-rule-type http"
expect_logged "keeps external ingress" "containerapp ingress enable --resource-group rg --name swiftcare-prod-frontend --type external --target-port 8080"
expect_equal "publishes the frontend address" "$(grep '^url=' "$GITHUB_OUTPUT" | cut -d= -f2-)" "https://swiftcare-prod-frontend.internal.example.test"
: > "$FAKE_AZ_LOG"
expect_success "updates the existing frontend app" deploy_frontend
expect_logged "updates instead of creating" "containerapp update --resource-group rg --name swiftcare-prod-frontend"
expect_not_logged "does not create it again" "containerapp create"
echo Stopped > "$FAKE_AZ_STATE/swiftcare-prod-frontend.running"
expect_success "starts a stopped frontend app" deploy_frontend
expect_equal "the stopped app is running again" "$(cat "$FAKE_AZ_STATE/swiftcare-prod-frontend.running")" "Running"
expect_failure "fails when the site keeps serving another commit" deploy_frontend FAKE_CURL_BODY="{\"sha\":\"$OLD\"}"
expect_failure "fails when the site serves no version" deploy_frontend FAKE_CURL_BODY=
expect_failure "needs the frontend app name" deploy_frontend AZURE_FRONTEND_APP=
: > "$FAKE_AZ_LOG"
expect_success "deploys the exact image digest CD built" deploy_frontend FRONTEND_IMAGE=ghcr.io/org/swiftcare-frontend@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
expect_logged "updates the app to the digest, not the tag" "--image ghcr.io/org/swiftcare-frontend@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
expect_failure "refuses an image from another repository" deploy_frontend FRONTEND_IMAGE=docker.io/other/frontend:latest

reset
add_app swiftcare-prod-frontend "$OLD" Running 8080
add_app swiftcare-prod-gateway "$OLD" Running 8000
expect_success "the guard succeeds with the frontend running" \
  env RESOURCE_GROUP=rg FRONTEND_APP=swiftcare-prod-frontend GITHUB_STEP_SUMMARY="" bash "$HERE/window-guard.sh" enforce
expect_not_logged "the guard leaves the public frontend running" "apps/swiftcare-prod-frontend/stop"
expect_logged "the guard still stops the gateway" "apps/swiftcare-prod-gateway/stop?api-version="

echo "== API docs switch (API_DOCS_ENABLED)"
reset
expect_success "deploys a service with the docs switch unset" bash "$HERE/deploy-service.sh" queue
expect_logged "turns the service docs off by default" "ApiDocs__Enabled=false"
reset
(API_DOCS_ENABLED=TRUE expect_success "deploys with a non-exact value" bash "$HERE/deploy-service.sh" queue)
expect_logged "treats anything but true as off" "ApiDocs__Enabled=false"
reset
(API_DOCS_ENABLED=true expect_success "deploys a service with the docs switch on" bash "$HERE/deploy-service.sh" queue)
expect_logged "publishes the service document when on" "ApiDocs__Enabled=true"
reset
(API_DOCS_ENABLED=true GATEWAY_ORIGIN=https://api.example.test expect_success "deploys the gateway with the docs switch on" bash "$HERE/deploy-gateway.sh")
expect_logged "turns the gateway docs pages on" "ApiDocs__Enabled=true"
expect_logged "sends try-it calls to the public gateway" "ApiDocs__PublicBaseUrl=https://api.example.test"
reset
(API_DOCS_ENABLED=false expect_success "deploys the gateway with the docs switch off" bash "$HERE/deploy-gateway.sh")
expect_logged "turns the gateway docs pages off" "ApiDocs__Enabled=false"

echo "== record-result"
record() { # record KEY JOB_STATUS DEPLOY ROLLBACK ROLLBACK_RESULT POST_SMOKE PREVIOUS
  KEY="$1" JOB_STATUS="$2" DEPLOY_OUTCOME="$3" ROLLBACK_OUTCOME="$4" ROLLBACK_RESULT="$5" POST_ROLLBACK_SMOKE="$6"     PREVIOUS_IMAGE="$7" DEPLOY_SHA=$NEW bash "$HERE/record-result.sh" "$WORK/results" > /dev/null
  jq -r '.status' "$WORK/results/$1.json"
}
rm -rf "$WORK/results"
expect_equal "success is Deployed" "$(record queue success success skipped "" skipped "ghcr.io/org/q:$OLD")" "Deployed"
expect_equal "failure before deploy leaves the app unchanged" "$(record queue failure skipped skipped "" skipped "ghcr.io/org/q:$OLD")" "Failed before deploy"
expect_equal "failed smoke check then rollback" "$(record queue failure success success "done" success "ghcr.io/org/q:$OLD")" "Rolled back"
expect_equal "records the commit it returned to" "$(jq -r '.previous' "$WORK/results/queue.json")" "$OLD"
expect_equal "notes a healthy app after rollback" "$(jq -r '.detail' "$WORK/results/queue.json" | grep -c 'pass again')" "1"
expect_equal "first deployment has nothing to roll back to" "$(record queue failure success success unavailable skipped "")" "Failed"
expect_equal "says why nothing was rolled back" "$(jq -r '.detail' "$WORK/results/queue.json" | grep -c 'No previous image')" "1"
expect_equal "a rollback to the same image is not reported as rolled back" "$(record queue failure success success same skipped "ghcr.io/org/q:$OLD")" "Failed"
expect_equal "says there was nothing to roll back to" "$(jq -r '.detail' "$WORK/results/queue.json" | grep -c 'nothing to roll back')" "1"
expect_equal "a failed rollback is reported" "$(record queue failure success failure "" skipped "ghcr.io/org/q:$OLD")" "Failed"
expect_equal "says the rollback failed" "$(jq -r '.detail' "$WORK/results/queue.json" | grep -c 'rollback itself failed')" "1"
expect_equal "a cancelled job is Cancelled" "$(record queue cancelled success skipped "" skipped "")" "Cancelled"
expect_equal "the frontend records plain failure" "$(record frontend failure n/a skipped "" skipped "")" "Failed"
expect_equal "the file is named after the key" "$(basename -a "$WORK"/results/*.json | sort | paste -sd' ')" "frontend.json queue.json"

echo
echo "$passed passed, $failed failed"
[ "$failed" -eq 0 ]

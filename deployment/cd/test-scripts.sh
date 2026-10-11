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
  "swiftcare-medical-record:5004" "swiftcare-prescription:5001"; do
  add_app "${entry%%:*}" "$OLD" Running "${entry##*:}"
done
add_app swiftcare-prescription "$OLD" Stopped 5001
add_app swiftcare-gateway "$OLD" Running 8000
expect_success "deploys the gateway" bash "$HERE/deploy-gateway.sh"
expect_equal "runs the new gateway image" "$(image_of swiftcare-gateway)" "ghcr.io/org/swiftcare-gateway:$NEW"
expect_equal "starts a stopped service the gateway routes to" "$(cat "$FAKE_AZ_STATE/swiftcare-prescription.running")" "Running"
expect_equal "leaves service images alone" "$(image_of swiftcare-auth)" "ghcr.io/org/swiftcare-auth:$OLD"
for cluster in auth patient queue medical-record prescription; do
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
  "swiftcare-medical-record:5004" "swiftcare-prescription:5001"; do
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
for target in auth patient queue medical-record prescription gateway; do
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
expect_failure "rejects an unknown target" bash "$HERE/smoke.sh" notification
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
(FRONTEND_WWW_ORIGIN="$FRONTEND_ORIGIN" expect_failure "rejects identical frontend origins" bash "$HERE/validate-config.sh" gateway)
(FRONTEND_ORIGIN=https://app.example.test/ expect_failure "rejects an origin with a trailing slash" bash "$HERE/validate-config.sh" gateway)
(GATEWAY_ORIGIN=http://api.example.test expect_failure "rejects a non-HTTPS gateway origin" bash "$HERE/validate-config.sh" frontend)
(unset AZURE_PRESCRIPTION_APP; expect_failure "plan requires every app name" bash "$HERE/validate-config.sh" plan)
expect_failure "rejects an unknown mode" bash "$HERE/validate-config.sh" everything

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

#!/usr/bin/env bash
# Smoke checks through the public gateway, using the synthetic smoke accounts.
# Usage: smoke.sh TARGET
#   TARGET is auth, patient, queue, medical-record, prescription, notification or gateway (all checks).
# Environment: GATEWAY_URL; SMOKE_RECEPTIONIST_USERNAME and SMOKE_RECEPTIONIST_PASSWORD for
# the auth, patient, queue, prescription, notification and gateway targets; SMOKE_DOCTOR_USERNAME and
# SMOKE_DOCTOR_PASSWORD for medical-record and gateway. Optional: SMOKE_FORCE_FAIL=TARGET fails
# that target on purpose (rollback drill); CD_SMOKE_ATTEMPTS and CD_SMOKE_INTERVAL tune retries.
# Response bodies and tokens are never printed. Every token is revoked again when the script ends.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

target="${1:?usage: smoke.sh auth|patient|queue|medical-record|prescription|notification|gateway}"
require_values GATEWAY_URL
GATEWAY_URL="${GATEWAY_URL%/}"
attempts="${CD_SMOKE_ATTEMPTS:-30}"
interval="${CD_SMOKE_INTERVAL:-10}"

body="$(mktemp)"
receptionist_token=""
doctor_token=""

logout_all() {
  local token
  for token in "$receptionist_token" "$doctor_token"; do
    [ -z "$token" ] && continue
    curl --silent --output /dev/null --max-time 20 --request POST \
      --header "Authorization: Bearer $token" "$GATEWAY_URL/api/auth/logout" || true
  done
  rm -f "$body"
}
trap logout_all EXIT

# http CURL_ARGS... : print the status code; the response body goes to $body only.
http() {
  curl --silent --output "$body" --write-out '%{http_code}' --max-time 20 "$@" || true
}

# check DESCRIPTION EXPECTED_CODE JQ_FILTER CURL_ARGS...
# Retry until the status matches and, when a filter is given, the body satisfies it.
check() {
  local description="$1" expected="$2" filter="$3" code="" attempt
  shift 3
  for attempt in $(seq 1 "$attempts"); do
    code=$(http "$@")
    if [ "$code" = "$expected" ] && { [ -z "$filter" ] || jq -e "$filter" "$body" >/dev/null 2>&1; }; then
      log "PASS  $description"
      return 0
    fi
    log "      $description returned ${code:-no response}; attempt $attempt/$attempts"
    [ "$attempt" -lt "$attempts" ] && sleep "$interval"
  done
  fail "$description: expected HTTP $expected, last response ${code:-none}"
}

# login ROLE : set the cached token for RECEPTIONIST or DOCTOR, retrying while services warm up.
login() {
  local role="$1" user_var pass_var payload code token attempt
  user_var="SMOKE_${role}_USERNAME"
  pass_var="SMOKE_${role}_PASSWORD"
  require_values "$user_var" "$pass_var"
  payload=$(jq -n --arg username "${!user_var}" --arg password "${!pass_var}" \
    '{username: $username, password: $password}')

  for attempt in $(seq 1 "$attempts"); do
    code=$(printf '%s' "$payload" | curl --silent --output "$body" --write-out '%{http_code}' \
      --max-time 20 --header 'Content-Type: application/json' --data @- \
      "$GATEWAY_URL/api/auth/login" || true)
    token=""
    [ "$code" = "200" ] && token=$(jq -r '.token // empty' "$body" 2>/dev/null || true)
    if [ -n "$token" ]; then
      printf '::add-mask::%s\n' "$token"
      if [ "$role" = "RECEPTIONIST" ]; then receptionist_token="$token"; else doctor_token="$token"; fi
      log "PASS  smoke $role account signs in"
      return 0
    fi
    log "      smoke $role sign-in returned ${code:-no response}; attempt $attempt/$attempts"
    [ "$attempt" -lt "$attempts" ] && sleep "$interval"
  done
  fail "The smoke $role account could not sign in through the gateway (HTTP ${code:-none})"
}

receptionist() { [ -n "$receptionist_token" ] || login RECEPTIONIST; }
doctor() { [ -n "$doctor_token" ] || login DOCTOR; }

check_gateway_health() {
  check "gateway health" 200 "" "$GATEWAY_URL/health"
}

check_auth() {
  check "auth rejects an invalid login" 401 "" \
    --header 'Content-Type: application/json' \
    --data '{"username":"deployment-smoke-test","password":"invalid"}' \
    "$GATEWAY_URL/api/auth/login"
  receptionist
}

check_patient() {
  receptionist
  check "patient search" 200 'type == "array"' \
    --header "Authorization: Bearer $receptionist_token" \
    "$GATEWAY_URL/api/patients/search?q=deployment-connectivity-check"
}

check_queue() {
  check "queue public display" 200 '
    type == "object"
    and (.currentRooms | type == "array")
    and (.nextQueueNumbers | type == "array")
    and all(.currentRooms[]; type == "object" and (.roomNumber | type == "string") and (.queueNumber | type == "string"))
    and all(.nextQueueNumbers[]; type == "string")' \
    "$GATEWAY_URL/api/queue/display"
  check "queue today rejects an anonymous request" 401 "" "$GATEWAY_URL/api/queue/today"
  receptionist
  check "queue today for a receptionist" 200 'type == "array"' \
    --header "Authorization: Bearer $receptionist_token" \
    "$GATEWAY_URL/api/queue/today"
}

check_medical_record() {
  doctor
  check "medical record templates for a doctor" 200 'type == "array"' \
    --header "Authorization: Bearer $doctor_token" \
    "$GATEWAY_URL/api/templates"
}

check_prescription() {
  receptionist
  check "pending prescriptions for a receptionist" 200 'type == "array"' \
    --header "Authorization: Bearer $receptionist_token" \
    "$GATEWAY_URL/api/prescriptions/pending"
}

check_notification() {
  receptionist
  check "notification feed for a receptionist" 200 'type == "array"' \
    --header "Authorization: Bearer $receptionist_token" \
    "$GATEWAY_URL/api/notifications/today"
}

if [ "${SMOKE_FORCE_FAIL:-}" = "$target" ]; then
  fail "Rollback drill: the $target smoke check was forced to fail"
fi

case "$target" in
  auth) check_auth ;;
  patient) check_patient ;;
  queue) check_queue ;;
  medical-record) check_medical_record ;;
  prescription) check_prescription ;;
  notification) check_notification ;;
  gateway)
    check_gateway_health
    check_auth
    check_patient
    check_queue
    check_medical_record
    check_prescription
    check_notification
    ;;
  *) fail "Unknown smoke target '$target'" ;;
esac

log "Smoke checks for $target passed."

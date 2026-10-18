#!/usr/bin/env bash
# Stop the billable runtime of one environment once its demo window has closed, so a forgotten
# window cannot run until the credit is gone. It only reads and stops; it never starts, creates,
# changes or deletes anything.
# Usage: window-guard.sh decide|enforce|cost
#   decide  : no Azure. WINDOW and WINDOW_UNTIL (the repository variables), ACTIVE_CD_RUNS (number
#             of CD runs not yet completed). Writes act=true|false and reason to $GITHUB_OUTPUT.
#   enforce : RESOURCE_GROUP, optional MYSQL_SERVER_NAME, KAFKA_APP and KAFKA_CONTAINER_GROUP, and
#             DRY_RUN. Stops every running Container App in the group (Kafka last), the legacy
#             Kafka container group and MySQL, then reads each one back.
#   cost    : AZURE_SUBSCRIPTION_ID, COST_SINCE (YYYY-MM-DD) and optional COST_WARN_USD. Reports
#             the subscription's actual cost since COST_SINCE and warns above the threshold.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

summary() {
  [ -z "${GITHUB_STEP_SUMMARY:-}" ] || printf '%s\n' "$*" >> "$GITHUB_STEP_SUMMARY"
  log "$*"
}

decide() {
  local state act reason
  state=$(window_state "${WINDOW:-}" "${WINDOW_UNTIL:-}")
  if [ "${ACTIVE_CD_RUNS:-0}" != "0" ]; then
    act=false
    reason="a CD run is in progress or queued; the guard checks again on its next run"
  elif [ "$state" = "open" ]; then
    act=false
    reason="the window is open until ${WINDOW_UNTIL}"
  else
    act=true
    reason="the window is ${state}"
  fi
  {
    echo "act=$act"
    echo "reason=$reason"
  } >> "${GITHUB_OUTPUT:-/dev/stdout}"
  summary "## Window guard"
  summary ""
  summary "Stop resources: $act ($reason)."
}

failures=0

# stop_one KIND NAME : stop one resource unless this is a dry run; count failures, keep going.
stop_one() {
  local kind="$1" name="$2"
  if [ "${DRY_RUN:-false}" = "true" ]; then
    summary "- Would stop $kind $name (dry run)."
    return
  fi
  case "$kind" in
    app) az containerapp stop --resource-group "$RESOURCE_GROUP" --name "$name" --output none ;;
    container-group) az container stop --resource-group "$RESOURCE_GROUP" --name "$name" --output none ;;
    mysql) az mysql flexible-server stop --resource-group "$RESOURCE_GROUP" --name "$name" --output none ;;
  esac && summary "- Stopped $kind $name." || {
    failures=$((failures + 1))
    summary "- Failed to stop $kind $name."
  }
}

enforce() {
  require_values RESOURCE_GROUP
  local running name state

  summary ""
  summary "### Actions in $RESOURCE_GROUP"
  summary ""

  # Applications first, then the broker they use.
  running=$(az containerapp list --resource-group "$RESOURCE_GROUP" \
    --query "[?properties.runningStatus=='Running'].name" --output tsv | tr -d '\r')
  for name in $running; do
    [ "$name" = "${KAFKA_APP:-}" ] || stop_one app "$name"
  done
  if [ -n "${KAFKA_APP:-}" ] && grep -qx "$KAFKA_APP" <<<"$running"; then
    stop_one app "$KAFKA_APP"
  fi

  if [ -n "${KAFKA_CONTAINER_GROUP:-}" ]; then
    state=$(az container show --resource-group "$RESOURCE_GROUP" --name "$KAFKA_CONTAINER_GROUP" \
      --query instanceView.state --output tsv 2>/dev/null | tr -d '\r' || true)
    [ "$state" != "Running" ] || stop_one container-group "$KAFKA_CONTAINER_GROUP"
  fi

  if [ -n "${MYSQL_SERVER_NAME:-}" ]; then
    state=$(az mysql flexible-server show --resource-group "$RESOURCE_GROUP" --name "$MYSQL_SERVER_NAME" \
      --query state --output tsv | tr -d '\r')
    [ "$state" != "Ready" ] || stop_one mysql "$MYSQL_SERVER_NAME"
  fi

  # Read back: anything still running is reported, whatever the reason.
  if [ "${DRY_RUN:-false}" != "true" ]; then
    running=$(az containerapp list --resource-group "$RESOURCE_GROUP" \
      --query "[?properties.runningStatus=='Running'].name" --output tsv | tr -d '\r')
    if [ -n "$running" ]; then
      failures=$((failures + 1))
      summary "- Still running after stop: $(tr '\n' ' ' <<<"$running")"
    else
      summary "- Read back: no Container App is running."
    fi
  fi

  [ "$failures" -eq 0 ] || fail "The window guard could not stop $failures resource(s); stop them manually"
}

cost() {
  require_values AZURE_SUBSCRIPTION_ID
  if [ -z "${COST_SINCE:-}" ]; then
    summary "Spend: not reported (set GUARD_COST_SINCE to the date the credit started)."
    return
  fi
  [[ "$COST_SINCE" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || fail "COST_SINCE must be YYYY-MM-DD"

  local body response total attempt
  body=$(jq -cn --arg from "${COST_SINCE}T00:00:00+00:00" --arg to "$(date -u +%Y-%m-%dT23:59:59+00:00)" \
    '{type: "ActualCost", timeframe: "Custom", timePeriod: {from: $from, to: $to},
      dataset: {granularity: "None", aggregation: {total: {name: "PreTaxCost", function: "Sum"}}}}')

  # Cost Management throttles often; retry a few times before giving up quietly.
  for attempt in 1 2 3; do
    if response=$(az rest --method post --headers "Content-Type=application/json" \
      --uri "https://management.azure.com/subscriptions/${AZURE_SUBSCRIPTION_ID}/providers/Microsoft.CostManagement/query?api-version=2023-11-01" \
      --body "$body" --output json 2>/dev/null); then
      break
    fi
    response=""
    sleep "${CD_WAIT_INTERVAL:-30}"
  done
  if [ -z "$response" ]; then
    warn "Spend could not be read from Cost Management; check the portal."
    return
  fi

  total=$(jq -r '((.properties.rows // [])[0][0] // 0) * 100 | round / 100' <<<"$response")
  summary ""
  summary "Spend since $COST_SINCE: USD $total."
  if [ -n "${COST_WARN_USD:-}" ] && jq -e --argjson total "$total" --argjson limit "$COST_WARN_USD" \
    -n '$total > $limit' >/dev/null; then
    warn "Spend since $COST_SINCE is USD $total, above the warning level of USD $COST_WARN_USD."
  fi
}

case "${1:-}" in
  decide) decide ;;
  enforce) enforce ;;
  cost) cost ;;
  *) fail "usage: window-guard.sh decide|enforce|cost" ;;
esac

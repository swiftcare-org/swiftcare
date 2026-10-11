#!/usr/bin/env bash
# Write one deployable's release result as JSON, for the final summary.
# Usage: record-result.sh OUTPUT_DIR
# Environment: KEY, JOB_STATUS (success|failure|cancelled), and for backend apps DEPLOY_OUTCOME,
# ROLLBACK_OUTCOME, ROLLBACK_RESULT (done|unavailable|same), POST_ROLLBACK_SMOKE, PREVIOUS_IMAGE and
# DEPLOY_SHA. The frontend passes DEPLOY_OUTCOME=n/a.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

output_dir="${1:?usage: record-result.sh OUTPUT_DIR}"
require_values KEY JOB_STATUS

# shellcheck disable=SC2153  # JOB_STATUS is the caller's variable, not a misspelling of job_status.
job_status="$JOB_STATUS"
deploy_outcome="${DEPLOY_OUTCOME:-n/a}"
rollback_outcome="${ROLLBACK_OUTCOME:-skipped}"
rollback_result="${ROLLBACK_RESULT:-}"
post_smoke="${POST_ROLLBACK_SMOKE:-skipped}"
previous_image="${PREVIOUS_IMAGE:-}"
release_sha="${DEPLOY_SHA:-}"

previous=""
if [[ "${previous_image##*:}" =~ ^[0-9a-f]{40}$ ]]; then previous="${previous_image##*:}"; fi

detail=""
if [ "$job_status" = "success" ]; then
  status="Deployed"
elif [ "$job_status" = "cancelled" ]; then
  status="Cancelled"
elif [ "$deploy_outcome" = "skipped" ]; then
  status="Failed before deploy"
  detail="The running version was not changed."
elif [ "$rollback_outcome" = "success" ] && [ "$rollback_result" = "done" ]; then
  status="Rolled back"
  detail="Back on ${previous:0:12}."
  case "$post_smoke" in
    success) detail="$detail Smoke checks pass again." ;;
    failure) detail="$detail Smoke checks still fail after the rollback." ;;
  esac
elif [ "$rollback_outcome" = "success" ] && [ "$rollback_result" = "unavailable" ]; then
  status="Failed"
  detail="No previous image existed, so it was left as deployed."
elif [ "$rollback_outcome" = "success" ] && [ "$rollback_result" = "same" ]; then
  status="Failed"
  detail="It already ran this image before the release, so there was nothing to roll back to."
elif [ "$rollback_outcome" = "failure" ]; then
  status="Failed"
  detail="The rollback itself failed. Check the app now."
else
  status="Failed"
fi

mkdir -p "$output_dir"
jq -n --arg key "$KEY" --arg status "$status" --arg detail "$detail" \
  --arg previous "$previous" --arg target "$release_sha" \
  '{key: $key, status: $status, detail: $detail, previous: $previous, target: $target}' \
  > "$output_dir/$KEY.json"
log "$KEY: $status${detail:+ - $detail}"

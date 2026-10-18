#!/usr/bin/env bash
# Decide which environment a CD run releases to and whether its demo window is open.
# Runs before any job that names a GitHub Environment, so a closed window never waits for an
# approval and never touches Azure.
#   After CI on develop -> development. After CI on main -> production.
#   Manual run          -> TARGET_INPUT (development by default); production only from main.
# Environment: EVENT_NAME, RUN_BRANCH (workflow_run head branch), REF_NAME, TARGET_INPUT,
# WINDOW_DEVELOPMENT and WINDOW_PRODUCTION (repository variables; "true" means open) and
# WINDOW_DEVELOPMENT_UNTIL and WINDOW_PRODUCTION_UNTIL (the window's UTC end time; see
# window_state in lib.sh).
# Writes environment, target and proceed to $GITHUB_OUTPUT.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

case "${EVENT_NAME:-}" in
  workflow_run)
    case "${RUN_BRANCH:-}" in
      main) target=production ;;
      develop) target=development ;;
      *) fail "CD runs after CI only on main or develop, not ${RUN_BRANCH:-an unknown branch}" ;;
    esac
    ;;
  workflow_dispatch)
    target="${TARGET_INPUT:-development}"
    case "$target" in
      development) ;;
      production)
        [ "${REF_NAME:-}" = "main" ] || fail "Production releases only from main; this run uses ${REF_NAME:-an unknown ref}"
        ;;
      *) fail "Unknown target '$target'; use development or production" ;;
    esac
    ;;
  *)
    fail "Unsupported event '${EVENT_NAME:-}'"
    ;;
esac

if [ "$target" = production ]; then
  window="${WINDOW_PRODUCTION:-}" until="${WINDOW_PRODUCTION_UNTIL:-}"
else
  window="${WINDOW_DEVELOPMENT:-}" until="${WINDOW_DEVELOPMENT_UNTIL:-}"
fi
window_variable="DEMO_WINDOW_$(printf '%s' "$target" | tr '[:lower:]' '[:upper:]')"
state=$(window_state "$window" "$until")

if [ "$state" = "open" ]; then
  proceed=true
  message="Releasing to $target (azure-$target): the demo window is open until $until."
else
  proceed=false
  message="No release: the $target demo window is ${state}. For an approved window set the repository variables $window_variable to true and ${window_variable}_UNTIL to its UTC end time (YYYY-MM-DDTHH:MMZ)."
  printf '::notice::%s\n' "$message"
fi

{
  echo "environment=azure-$target"
  echo "target=$target"
  echo "proceed=$proceed"
} >> "${GITHUB_OUTPUT:-/dev/stdout}"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  printf '## Release target\n\n%s\n' "$message" >> "$GITHUB_STEP_SUMMARY"
fi
log "$message"

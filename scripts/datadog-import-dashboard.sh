#!/usr/bin/env bash
# F4-37 — creates or updates the saga dashboard in Datadog from observability/datadog/saga-dashboard.json.
#
# Usage:
#   DD_API_KEY=... DD_APP_KEY=... scripts/datadog-import-dashboard.sh             # creates it, prints the id
#   DD_API_KEY=... DD_APP_KEY=... DASHBOARD_ID=abc-123-xyz scripts/datadog-import-dashboard.sh   # updates it
#
# DD_SITE defaults to datadoghq.com (use us5.datadoghq.com, datadoghq.eu, ... for other regions).
# DD_APP_KEY needs the dashboards_write scope.

set -euo pipefail

: "${DD_API_KEY:?set DD_API_KEY}"
: "${DD_APP_KEY:?set DD_APP_KEY}"
DD_SITE="${DD_SITE:-datadoghq.com}"
DASHBOARD_FILE="$(cd "$(dirname "$0")/.." && pwd)/observability/datadog/saga-dashboard.json"

if [[ -n "${DASHBOARD_ID:-}" ]]; then
  method=PUT url="https://api.${DD_SITE}/api/v1/dashboard/${DASHBOARD_ID}"
else
  method=POST url="https://api.${DD_SITE}/api/v1/dashboard"
fi

response=$(curl -sS -w '\n%{http_code}' -X "$method" "$url" \
  -H 'Content-Type: application/json' \
  -H "DD-API-KEY: ${DD_API_KEY}" \
  -H "DD-APPLICATION-KEY: ${DD_APP_KEY}" \
  --data-binary "@${DASHBOARD_FILE}")

status=${response##*$'\n'}
body=${response%$'\n'*}

if [[ "$status" != 200 ]]; then
  echo "Datadog returned HTTP $status:" >&2
  echo "$body" >&2
  exit 1
fi

id=$(sed -n 's/.*"id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' <<<"$body" | head -1)
[[ "$DD_SITE" == datadoghq.com ]] && app="app.datadoghq.com" || app="$DD_SITE"

if [[ -n "${DASHBOARD_ID:-}" ]]; then
  echo "Dashboard updated: https://${app}/dashboard/${id}"
else
  echo "Dashboard created: https://${app}/dashboard/${id}"
  echo "Next time, pass DASHBOARD_ID=${id} to update it instead of creating a copy."
fi

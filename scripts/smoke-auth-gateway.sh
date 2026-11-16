#!/usr/bin/env bash
# F4-13 — checks that CPF/JWT auth works end to end through the deployed API Gateway.
#
# Usage:
#   API_URL=https://<id>.execute-api.<region>.amazonaws.com \
#   CPF=11144477735 \
#   ORDER_ID=<an order of that customer> \
#   OTHER_ORDER_ID=<an order of another customer> \
#   STAFF_EMAIL=admin@mechanics.local STAFF_PASSWORD='...' \
#   scripts/smoke-auth-gateway.sh
#
# API_URL comes from `terraform output api_gateway_url` in mechanics-infra-k8s.
# OTHER_ORDER_ID, STAFF_EMAIL and STAFF_PASSWORD are optional; their checks are skipped when unset.
# Exits non-zero on the first failed check.

set -euo pipefail

: "${API_URL:?set API_URL}"
: "${CPF:?set CPF}"
: "${ORDER_ID:?set ORDER_ID}"
API_URL="${API_URL%/}"

pass() { printf '  \033[32mPASS\033[0m %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m %s\n' "$1"; exit 1; }

# status <method> <path> [token] [json body]
status() {
  local args=(-s -o /dev/null -w '%{http_code}' -X "$1" "$API_URL$2")
  [[ -n "${3:-}" ]] && args+=(-H "Authorization: Bearer $3")
  [[ -n "${4:-}" ]] && args+=(-H 'Content-Type: application/json' -d "$4")
  curl "${args[@]}"
}

# expect <description> <actual> <allowed codes...>
expect() {
  local desc=$1 actual=$2; shift 2
  for code in "$@"; do [[ "$actual" == "$code" ]] && { pass "$desc ($actual)"; return; }; done
  fail "$desc — expected $*, got $actual"
}

token_from() { sed -n 's/.*"token"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p'; }

echo "Gateway: $API_URL"

echo "Public routes"
expect "GET /health is reachable without a token" "$(status GET /health)" 200

echo "Authorizer"
expect "no token is rejected" "$(status GET "/api/service-orders/$ORDER_ID/status")" 401 403
expect "forged token is rejected" "$(status GET "/api/service-orders/$ORDER_ID/status" 'eyJhbGciOiJIUzI1NiJ9.e30.invalid')" 401 403

echo "Customer login (CPF Lambda)"
expect "invalid CPF is refused" "$(status POST /auth '' '{"cpf":"00000000000"}')" 400
customer_token=$(curl -s -X POST "$API_URL/auth" -H 'Content-Type: application/json' -d "{\"cpf\":\"$CPF\"}" | token_from)
[[ -n "$customer_token" ]] && pass "POST /auth returned a token" || fail "POST /auth returned no token for CPF $CPF"

echo "Customer access in the OS Service"
expect "own order status" "$(status GET "/api/service-orders/$ORDER_ID/status" "$customer_token")" 200
expect "own order history" "$(status GET "/api/service-orders/$ORDER_ID/history" "$customer_token")" 200
expect "staff-only endpoint is forbidden" "$(status GET /api/service-orders "$customer_token")" 403
if [[ -n "${OTHER_ORDER_ID:-}" ]]; then
  expect "another customer's order is hidden" "$(status GET "/api/service-orders/$OTHER_ORDER_ID/status" "$customer_token")" 404
else
  echo "  SKIP another customer's order (OTHER_ORDER_ID not set)"
fi

if [[ -n "${STAFF_EMAIL:-}" && -n "${STAFF_PASSWORD:-}" ]]; then
  echo "Staff access"
  staff_token=$(curl -s -X POST "$API_URL/api/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$STAFF_EMAIL\",\"password\":\"$STAFF_PASSWORD\"}" | token_from)
  [[ -n "$staff_token" ]] && pass "POST /api/auth/login returned a token" || fail "staff login returned no token"
  expect "staff reads the order" "$(status GET "/api/service-orders/$ORDER_ID" "$staff_token")" 200
  expect "staff lists orders" "$(status GET /api/service-orders "$staff_token")" 200
else
  echo "  SKIP staff checks (STAFF_EMAIL/STAFF_PASSWORD not set)"
fi

echo "All checks passed."

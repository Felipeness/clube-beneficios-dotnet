#!/usr/bin/env bash
# Plays the Pix provider against the local API: signs a pix.received event (HMAC-SHA256 of "{timestamp}.{body}")
# and posts it, since Scalar cannot sign requests.
# Usage: scripts/send-pix-webhook.sh <membershipId> [period yyyy-MM] [endToEndId] [amountCents]
# Re-run with the same endToEndId to see a redelivery; with another one for the same period to see duplicate_payment.
set -euo pipefail

membership_id="${1:?usage: $0 <membershipId> [period] [endToEndId] [amountCents]}"
period="${2:-$(TZ=America/Sao_Paulo date +%Y-%m)}"
end_to_end_id="${3:-E$(openssl rand -hex 16 | cut -c1-31)}"
amount_cents="${4:-2000}"
# The Development secret from src/Clube.Host/appsettings.Development.json; override it to hit another environment.
secret="${PIX_WEBHOOK_SECRET:-dev-only-pix-webhook-secret-never-use-in-production}"
url="${PIX_WEBHOOK_URL:-http://localhost:5135/webhooks/pix}"

timestamp="$(date +%s)"
paid_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
body="$(printf '{"type":"pix.received","endToEndId":"%s","membershipId":"%s","period":"%s","amountCents":%s,"paidAt":"%s"}' \
  "$end_to_end_id" "$membership_id" "$period" "$amount_cents" "$paid_at")"
signature="$(printf '%s.%s' "$timestamp" "$body" | openssl dgst -sha256 -hmac "$secret" -hex | sed 's/^.*= //')"

echo "endToEndId: $end_to_end_id" >&2
curl --silent --show-error --max-time 10 -X POST "$url" \
  -H 'Content-Type: application/json' \
  -H "X-Pix-Timestamp: $timestamp" \
  -H "X-Pix-Signature: $signature" \
  --data-binary "$body"
echo

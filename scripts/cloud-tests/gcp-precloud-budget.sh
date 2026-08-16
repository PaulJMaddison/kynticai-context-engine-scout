#!/usr/bin/env bash
set -euo pipefail

: "${GCP_PROJECT_ID:?Set GCP_PROJECT_ID.}"
: "${GCP_BILLING_ACCOUNT_ID:?Set GCP_BILLING_ACCOUNT_ID.}"

SCOUT_BUDGET_AMOUNT="${SCOUT_BUDGET_AMOUNT:-25}"
SCOUT_BUDGET_NAME="${SCOUT_BUDGET_NAME:-Scout precloud ${GCP_PROJECT_ID}}"

if [[ "${SCOUT_BUDGET_AMOUNT}" != "25" ]]; then
  echo "Refusing budget amount '${SCOUT_BUDGET_AMOUNT}'. The checked-in precloud guardrail is fixed at 25 billing-account currency units." >&2
  exit 2
fi

# A Cloud Billing budget is an alerting guardrail, not a hard service cutoff. The actual hard controls
# for this test are enforced by gcp-precloud-setup.sh: one whitelisted CPU VM, no GPU, 2h max runtime,
# automatic VM deletion, and explicit teardown.
existing=$(gcloud billing budgets list \
  --billing-account="${GCP_BILLING_ACCOUNT_ID}" \
  --filter="displayName='${SCOUT_BUDGET_NAME}'" \
  --format='value(name)' \
  --limit=1)

if [[ -n "${existing}" ]]; then
  echo "Budget already exists: ${existing}"
  exit 0
fi

gcloud billing budgets create \
  --billing-account="${GCP_BILLING_ACCOUNT_ID}" \
  --display-name="${SCOUT_BUDGET_NAME}" \
  --budget-amount="${SCOUT_BUDGET_AMOUNT}" \
  --filter-projects="projects/${GCP_PROJECT_ID}" \
  --threshold-rule=percent=0.50 \
  --threshold-rule=percent=0.80 \
  --threshold-rule=percent=1.00

echo "Created alerting budget '${SCOUT_BUDGET_NAME}' for project ${GCP_PROJECT_ID}."
echo "IMPORTANT: Google Cloud budgets alert; they do not automatically stop resources."

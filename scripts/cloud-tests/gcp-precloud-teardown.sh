#!/usr/bin/env bash
set -euo pipefail

: "${GCP_PROJECT_ID:?Set GCP_PROJECT_ID.}"

GCP_ZONE="${GCP_ZONE:-europe-west2-b}"
SCOUT_VM_NAME="${SCOUT_VM_NAME:-scout-precloud-test}"

# Deliberately delete only the resource created by gcp-precloud-setup.sh. Project deletion is never
# implicit because a project can contain unrelated resources even when it was intended to be disposable.
gcloud config set project "${GCP_PROJECT_ID}" >/dev/null

if gcloud compute instances describe "${SCOUT_VM_NAME}" --zone "${GCP_ZONE}" >/dev/null 2>&1; then
  gcloud compute instances delete "${SCOUT_VM_NAME}" --zone "${GCP_ZONE}" --quiet
else
  echo "VM ${SCOUT_VM_NAME} is already absent."
fi

remaining=$(gcloud compute instances list \
  --filter='labels.purpose=scout-precloud AND labels.ephemeral=true' \
  --format='value(name,zone.basename())')

if [[ -n "${remaining}" ]]; then
  echo "WARNING: other Scout precloud-labelled VMs still exist:" >&2
  echo "${remaining}" >&2
  exit 4
fi

echo "Scout precloud VM teardown complete."
echo "Review Billing/Asset Inventory for project ${GCP_PROJECT_ID} before considering the test closed."

#!/usr/bin/env bash
set -euo pipefail

: "${GCP_PROJECT_ID:?Set GCP_PROJECT_ID to a dedicated or disposable Google Cloud project.}"

GCP_ZONE="${GCP_ZONE:-europe-west2-b}"
SCOUT_VM_NAME="${SCOUT_VM_NAME:-scout-precloud-test}"
SCOUT_MACHINE_TYPE="${SCOUT_MACHINE_TYPE:-e2-standard-4}"
SCOUT_BOOT_DISK_SIZE="${SCOUT_BOOT_DISK_SIZE:-50GB}"
SCOUT_MAX_RUN_DURATION="${SCOUT_MAX_RUN_DURATION:-2h}"

case "${SCOUT_MACHINE_TYPE}" in
  e2-standard-4|e2-standard-8) ;;
  *)
    echo "Refusing machine type '${SCOUT_MACHINE_TYPE}'. Allowed test types: e2-standard-4, e2-standard-8." >&2
    exit 2
    ;;
esac

if [[ "${SCOUT_MAX_RUN_DURATION}" != "2h" ]]; then
  echo "Refusing max run duration '${SCOUT_MAX_RUN_DURATION}'. This test harness is deliberately capped at 2h." >&2
  exit 2
fi

gcloud config set project "${GCP_PROJECT_ID}" >/dev/null
gcloud services enable compute.googleapis.com

if gcloud compute instances describe "${SCOUT_VM_NAME}" --zone "${GCP_ZONE}" >/dev/null 2>&1; then
  echo "Refusing to reuse existing VM ${SCOUT_VM_NAME}. Delete it or choose a different SCOUT_VM_NAME." >&2
  exit 3
fi

echo "Creating ephemeral Scout validation VM:"
echo "  project: ${GCP_PROJECT_ID}"
echo "  zone: ${GCP_ZONE}"
echo "  machine: ${SCOUT_MACHINE_TYPE}"
echo "  max runtime: ${SCOUT_MAX_RUN_DURATION}"
echo "  termination: DELETE"

gcloud compute instances create "${SCOUT_VM_NAME}" \
  --zone="${GCP_ZONE}" \
  --machine-type="${SCOUT_MACHINE_TYPE}" \
  --image-family=debian-12 \
  --image-project=debian-cloud \
  --boot-disk-type=pd-balanced \
  --boot-disk-size="${SCOUT_BOOT_DISK_SIZE}" \
  --max-run-duration="${SCOUT_MAX_RUN_DURATION}" \
  --instance-termination-action=DELETE \
  --labels=purpose=scout-precloud,ephemeral=true \
  --metadata=enable-oslogin=TRUE

cat <<EOF

VM created. It will be deleted automatically after ${SCOUT_MAX_RUN_DURATION} of runtime.
No application port has been opened by this script.

Next:
  GCP_PROJECT_ID=${GCP_PROJECT_ID} GCP_ZONE=${GCP_ZONE} SCOUT_VM_NAME=${SCOUT_VM_NAME} \\
    bash scripts/cloud-tests/gcp-precloud-run.sh

Always finish with:
  GCP_PROJECT_ID=${GCP_PROJECT_ID} GCP_ZONE=${GCP_ZONE} SCOUT_VM_NAME=${SCOUT_VM_NAME} \\
    bash scripts/cloud-tests/gcp-precloud-teardown.sh
EOF

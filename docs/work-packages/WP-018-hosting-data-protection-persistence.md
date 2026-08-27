# WP-018 — Make hosted Data Protection persistence real

## Metadata

- **Status:** Planned
- **Priority:** Critical before real hosted connector credentials
- **Phase:** F — Production correctness
- **Depends on:** WP-016
- **Review gate:** xhigh (credential safety, deployment)

## Context

Scout protects stored connector credentials using ASP.NET Data Protection. Production correctly requires a persistent key-ring path.

Docker Compose mounts persistent storage for the key ring. The checked-in Render Blueprint sets a persistent-looking path but does not clearly provision/mount durable storage for it.

A readiness validator can validate a path string; it cannot prove the hosting platform actually persists that path. A restart/redeploy with a lost key ring can make existing encrypted connector credentials unreadable.

## Objective

Make every supported hosted deployment example satisfy the real cryptographic persistence requirement, not merely the configuration-shape check.

## Tasks

1. Verify current Render disk/persistent-storage support and the exact Blueprint syntax used by this repository's deployment target.
2. Add durable storage for the Data Protection key ring, or explicitly remove Render from supported credential-bearing production deployment until a durable key mechanism is configured.
3. Ensure backup/restore guidance includes both Scout DB state and the matching Data Protection key material.
4. Add a deployment rehearsal:
   - store a connector credential;
   - restart/redeploy the API;
   - resolve the same credential successfully.
5. Ensure readiness/docs distinguish "path configured" from "persistence externally guaranteed".
6. Review other checked-in deployment examples for the same issue.
7. Never print or export key material during proof.

## Acceptance criteria

- [ ] Every production-supported deployment path has a durable key-ring story.
- [ ] A restart/redeploy proof demonstrates previously protected credentials remain decryptable.
- [ ] No documentation claims path configuration alone guarantees persistence.
- [ ] Key material remains excluded from source control/logs/support bundles.

## Verification

Run the credential round-trip/restart proof on disposable infrastructure and record only non-secret evidence. Include the result in the final GCP sign-off evidence.

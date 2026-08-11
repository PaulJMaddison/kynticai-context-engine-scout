---
title: Installation
description: How to install and set up KynticAI Scout for local development.
---

## Prerequisites

Choose **one** of the following:

| Approach | Requirements |
|---|---|
| **Docker (recommended)** | Docker Engine 24+ and Docker Compose v2 |
| **Local (.NET SDK)** | .NET 10.0 SDK, Node.js 22+ (or let the setup scripts install repo-local copies) |

## Clone the Repository

```bash
git clone https://github.com/PaulJMaddison/kynticai-context-engine-scout.git
cd scout
```

## Option 1 — Docker Demo Stack (Recommended)

The Docker demo stack starts the full evaluation environment: PostgreSQL,
the API, the web console, and observability, all with seeded demo data.
This is the recommended path for evaluating KynticAI Scout.

### Linux / macOS

```bash
sh ./scripts/start-scout-docker.sh --reset
```

### Windows (PowerShell)

```powershell
.\scripts\start-scout-docker.ps1 -Reset
```

The stack exposes the API at [http://127.0.0.1:5198](http://127.0.0.1:5198)
and the web console at [http://127.0.0.1:5173](http://127.0.0.1:5173).

## Option 2 — API-Only Quickstart (Docker)

For a fast API-only smoke test with no web console, run the API in a single
container with no external dependencies:

```bash
docker compose -f deploy/docker-compose.yml up scout-api --build
```

The API starts on [http://127.0.0.1:8080](http://127.0.0.1:8080) with seeded
demo data and a SQLite database.

### Docker with PostgreSQL

For a production-style setup with PostgreSQL:

```bash
cp deploy/.env.example deploy/.env
# Edit deploy/.env with production values
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile postgres up --build
```

## Option 3 — Local Scripts (Contributor Path)

If you are contributing to the repository, the local setup scripts download
repo-local .NET and Node.js runtimes, install npm dependencies, build the
solution, seed demo data into a local SQLite database, and prepare the admin
console. No global .NET SDK or Node.js install is required.

### Linux / macOS

```bash
sh ./scripts/setup-demo.sh
```

### Windows (PowerShell)

```powershell
.\scripts\setup-demo.ps1
```

This typically takes around two minutes on a fresh clone.

## Verify the Installation

After setup completes, check that the API is responding. Use the port that
matches the path you chose: `5198` for the Docker demo stack, `8080` for the
API-only quickstart.

### Docker demo stack

```bash
curl http://127.0.0.1:5198/health/ready
```

### API-only quickstart

```bash
curl http://127.0.0.1:8080/health/ready
```

Expected response:

```json
{
  "status": "ok",
  "service": "KynticAI.Scout.Api",
  "checks": [
    { "name": "scout-db", "status": "ok" },
    { "name": "customer-ops-db", "status": "ok" }
  ]
}
```

The check names reflect the databases configured for the path you chose.

## What's Next?

Follow the [Quickstart](/getting-started/quickstart/) to start the demo
and make your first API calls.

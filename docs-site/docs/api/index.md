---
sidebar_position: 1
title: API Reference
---

# API Reference

Weir exposes a REST API from its .NET server. The API is served at `/api/v1` under the same origin as the web UI.

## OpenAPI specification

The OpenAPI document is a hand-maintained contract committed to the repository. The server embeds it and serves the operations it implements:

- **Running server**: `http://localhost:9347/openapi.json`
- **Source**: [`apps/web/openapi/weir-openapi.json`](https://github.com/jampat000/Weir/blob/main/apps/web/openapi/weir-openapi.json)

When you change an endpoint's request or response shape, update this file in the same change.

## Authentication

Weir uses cookie-based sessions with CSRF protection:

- First-run bootstrap creates the admin user
- Login via `POST /api/v1/auth/login`
- Session cookies are HTTP-only and secure (when behind HTTPS)
- State-changing requests require CSRF tokens

## Key endpoints

### Health and readiness

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/health` | GET | No | Process liveness check |
| `/ready` | GET | No | Full readiness (DB, migrations) |
| `/metrics` | GET | Token | Prometheus metrics |

### Processing

| Prefix | Description |
|--------|-------------|
| `/api/v1/processing/libraries` | Libraries and their order, rule previews, and the files already in each library: scan, clean, leave alone, schedule |
| `/api/v1/processing/files` | Files Weir has picked up: tracks, logs, requeue, move to top, why a file is held |
| `/api/v1/processing/rule-sets` | Audio and subtitle rules |
| `/api/v1/processing/jobs` | Queue and inspect background jobs |
| `/api/v1/processing/maintenance` | Cleanup settings, and running cleanup now |
| `/api/v1/processing/metadata-provider` | The TMDb connection, used by rules that keep a title's original language |
| `/api/v1/processing/` (other) | Performance and runtime settings, hardware, direct-play devices, media manager setup checks, overview counts |
| `/api/v1/pause` | The one pause switch for processing |

### Media managers

| Prefix | Description |
|--------|-------------|
| `/api/v1/media-managers/` | Connections to Sonarr, Radarr, Deluno and other tools: add, test, searches, webhook secret |
| `/api/v1/intake/` | Hand-offs from media managers: the webhook, each hand-off's status, and its outcome |

### Platform

| Prefix | Description |
|--------|-------------|
| `/api/v1/auth/` | First-run bootstrap, sign-in and sign-out, CSRF token, password and username changes, sessions |
| `/api/v1/activity/` | Events and per-file history, the live event stream, export |
| `/api/v1/suite/` | App settings, alert channels, backups and the configuration bundle, updates, logs, metrics, security overview |
| `/api/v1/system/` | Runtime directories, media tools, signed-in readiness, reconciliation report and repair |

## TypeScript types

The frontend generates TypeScript types from the committed OpenAPI document:

```bash
cd apps/web
npm run api:types:generate
```

This produces typed API clients in `apps/web/src/lib/api/generated/openapi-types.ts`. CI runs `npm run api:types:check` to make sure the generated types match the document.

# Docker (all-in-one)

One container runs **FastAPI + SQLite + bundled web UI** on port **8788**. The UI calls **`/api/v1`** on the same origin (no CORS friction).

This path is **alpha**: validated in CI patterns, but not the primary release vehicle — see [release.md](release.md).

## Quick start (Compose, recommended)

From the **repository root**:

```bash
cp docker/mediamop.env.example .env.mediamop
# Edit .env.mediamop — set MEDIAMOP_SESSION_SECRET to a long random value (32+ characters).

docker compose --env-file .env.mediamop -f docker-compose.mediamop.yml up --build
```

Open **`http://localhost:8788/`**. On a **fresh volume**, the app sends you through **first-time admin setup** (bootstrap) before normal sign-in.

Stop: `Ctrl+C` or `docker compose ... down`. Data stays in the **`mediamop_data`** volume until you remove it.

## Quick start (`docker run`)

```bash
docker build -t mediamop:local .

docker run --rm \
  -e MEDIAMOP_SESSION_SECRET="$(openssl rand -hex 32)" \
  -p 8788:8788 \
  -v mediamop-home:/data/mediamop \
  mediamop:local
```

## Pull from GitHub Container Registry (GHCR)

After the **Docker alpha** workflow succeeds, images live at:

`ghcr.io/<github-owner-lowercase>/<repo-lowercase>:<tag>`

If the package is **private**, authenticate once:

```bash
echo "<GITHUB_PAT_WITH_read:packages>" | docker login ghcr.io -u <github-username> --password-stdin
docker pull ghcr.io/<owner>/<repo>:alpha
```

Then run the same `docker run` as above, swapping the image name. Make the package **public** under **GitHub → Packages → Package settings** if you want anonymous `docker pull`.

## HTTPS and session cookies

The image sets **`MEDIAMOP_SESSION_COOKIE_SECURE=false`** by default so **HTTP** (`localhost`, LAN IP) **sign-in works**.

When **every** user reaches MediaMop over **HTTPS** (TLS at Caddy, nginx, Traefik, etc.), set:

```yaml
environment:
  MEDIAMOP_SESSION_COOKIE_SECURE: "true"
```

(or `-e MEDIAMOP_SESSION_COOKIE_SECURE=true` with `docker run`).

## Environment variables

| Variable | Required | Notes |
|----------|----------|--------|
| `MEDIAMOP_SESSION_SECRET` | **Yes** | **≥ 32 characters.** Used for sessions and Fernet-style key material. |
| `MEDIAMOP_HOME` | No | Defaults to `/data/mediamop` (SQLite and runtime files). Mount a volume here for persistence. |
| `PORT` | No | Uvicorn listen port inside the container (default **8788**). |
| `MEDIAMOP_SESSION_COOKIE_SECURE` | No | Default **false** in the image; set **true** when all clients use HTTPS. |
| `MEDIAMOP_WEB_DIST` | No | Pre-set in the image to the bundled `dist/`; override only for debugging. |
| `MEDIAMOP_ENV` | No | Default **production** in the image. |

Other knobs (worker counts, log level, Fetcher/Refiner settings, …) use the same **`MEDIAMOP_*`** variables as [local-development.md](local-development.md).

## CI: build and push

Workflow **Docker alpha** (`.github/workflows/docker-alpha.yml`):

1. **Actions → Docker alpha → Run workflow** — optional tag (default **`alpha`**).
2. Push a git tag matching **`v*-alpha*`** (e.g. `v0.1.0-alpha.1`) to build and push that tag name.

## Health check

The image defines a **`HEALTHCHECK`** against **`GET /health`**. Orchestrators and `docker ps` use it for readiness.

## Optional Postgres compose

The root **`docker-compose.yml`** starts **PostgreSQL only** for experiments; the MediaMop API **does not** use it in the default SQLite-first setup.

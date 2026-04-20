# MediaMop — Docker (alpha testers)

MediaMop is **Windows-first** for day-to-day development; this Docker image exists so **Linux/macOS/Windows** testers can run an **all-in-one** build (API + production web UI + SQLite) without the full Python/Node dev stack.

**Status:** **Alpha** — same codebase as `main`, but the container path is newer than the PowerShell + `npm run dev` workflow. Report issues with repro steps and logs.

---

## What you need installed

| Requirement | Notes |
|-------------|--------|
| **Docker Engine** + **Buildx** | Docker Desktop (Windows/macOS) or Docker Engine + Compose plugin (Linux) is enough. |
| **Docker Compose v2** | `docker compose version` should work (not only legacy `docker-compose`). |
| **Git** | To clone the repo if you build from source. |
| **A shell that can run the commands below** | Examples use `bash`. On Windows: **Git Bash**, **WSL**, or translate to PowerShell. |
| **`openssl`** (optional) | Convenient for generating `MEDIAMOP_SESSION_SECRET`; any cryptographically random 32+ character string is fine. |

You do **not** need Python or Node on the host if you **pull a pre-built image** from GHCR (see below). You **do** need them only if you choose to **never** use our CI image and always `docker build` locally (unusual).

---

## What you must configure

### 1. `MEDIAMOP_SESSION_SECRET` (required)

- **At least 32 characters.**
- Used for **sessions** and **server-side crypto** (e.g. stored API keys).
- **Never commit** this value; keep it in a local env file or secret store.

Generate one example:

```bash
openssl rand -hex 32
```

### 2. Persistent data (strongly recommended)

SQLite and runtime files live under **`MEDIAMOP_HOME`** inside the container (default **`/data/mediamop`**).

- Use a **named volume** or **bind mount** so data survives container recreation.
- If you skip a volume, data is lost when the container is removed.

### 3. HTTP vs HTTPS (session cookies)

The image defaults **`MEDIAMOP_SESSION_COOKIE_SECURE=false`** so **sign-in works on plain `http://`** (e.g. `http://localhost:8788`, `http://192.168.x.x:8788`).

If **every** user reaches MediaMop over **HTTPS** (TLS terminated at a reverse proxy), set:

```text
MEDIAMOP_SESSION_COOKIE_SECURE=true
```

Otherwise browsers will not send the session cookie on `http://` and login will fail.

---

## Recommended: Docker Compose (from repo clone)

**Working directory:** repository **root** (where `docker-compose.mediamop.yml` lives).

1. Copy the example env file:

   ```bash
   cp docker/mediamop.env.example .env.mediamop
   ```

2. Edit **`.env.mediamop`** and set **`MEDIAMOP_SESSION_SECRET`** to a long random value (32+ characters).

3. Start the stack:

   ```bash
   docker compose --env-file .env.mediamop -f docker-compose.mediamop.yml up --build
   ```

4. Open **`http://localhost:8788/`** in a browser.

**First run:** On an **empty** data directory, the UI walks through **first-time admin setup** (bootstrap). After that you sign in normally.

**Stop:** `Ctrl+C` or:

```bash
docker compose --env-file .env.mediamop -f docker-compose.mediamop.yml down
```

Data remains in the **`mediamop_data`** Compose volume until you remove it (`docker volume rm …`).

---

## Alternative: `docker run` (local image)

From repo root after `docker build`:

```bash
docker build -t mediamop:local .

docker run --rm \
  -e MEDIAMOP_SESSION_SECRET="$(openssl rand -hex 32)" \
  -p 8788:8788 \
  -v mediamop-home:/data/mediamop \
  mediamop:local
```

Same URL: **`http://localhost:8788/`**.

---

## Pull a pre-built image (GitHub Container Registry)

Maintainers publish images via **GitHub Actions → “Docker alpha”** (or tags like `v0.1.0-alpha.1`). Image reference shape:

```text
ghcr.io/<github-owner-lowercase>/<repo-lowercase>:<tag>
```

**If the package is private**, authenticate before `docker pull`:

```bash
echo "<GITHUB_PAT_WITH_read:packages>" | docker login ghcr.io -u <github-username> --password-stdin
docker pull ghcr.io/<owner>/<repo>:alpha
```

Then run with `docker run` (same `-e`, `-p`, `-v` as above) using that image name instead of `mediamop:local`.

**Public pulls:** In GitHub → **Packages** → package **Settings** → visibility **Public**, if you want anonymous `docker pull`.

---

## Ports and health

| Port | Service |
|------|---------|
| **8788** | HTTP — API (`/api/v1`, `/health`) and static UI (`/`) |

The image includes a **`HEALTHCHECK`** on **`GET /health`**. `docker ps` shows `healthy` once migrations finished and uvicorn is up.

---

## Environment reference (common)

| Variable | Required | Default in image | Notes |
|----------|----------|------------------|--------|
| `MEDIAMOP_SESSION_SECRET` | **Yes** | — | ≥ 32 chars. |
| `MEDIAMOP_HOME` | No | `/data/mediamop` | SQLite + runtime files. |
| `PORT` | No | `8788` | Listen port **inside** the container. |
| `MEDIAMOP_SESSION_COOKIE_SECURE` | No | `false` | Set `true` when all clients use HTTPS. |
| `MEDIAMOP_WEB_DIST` | No | set | Points at bundled `dist/`; override only for debugging. |
| `MEDIAMOP_ENV` | No | `production` | Matches packaged defaults. |

Other **`MEDIAMOP_*`** settings (worker counts, Fetcher, Refiner, etc.) follow the same names as in **[`docs/local-development.md`](../docs/local-development.md)**.

---

## What Docker does **not** do for you

The container **starts MediaMop**; it does **not**:

- Install or configure **Sonarr**, **Radarr**, Jellyfin, etc.
- Open firewall ports on your LAN (you must allow **8788** or your mapped port).
- Replace **Windows-first** developer docs — for feature work, use **[`README.md`](../README.md)** and **`docs/local-development.md`**.

---

## Files in this folder

| File | Purpose |
|------|---------|
| `mediamop.env.example` | Copy to `.env.mediamop` (or similar) for Compose. |
| `entrypoint.sh` | Container entry: checks secret, runs **Alembic**, starts **uvicorn**. |

Repo root also has **`Dockerfile`**, **`docker-compose.mediamop.yml`**, and **`.github/workflows/docker-alpha.yml`** (CI build/push).

---

## More detail

Release policy (tags, zip artifacts): **[`docs/release.md`](../docs/release.md)**.

Optional Postgres-only compose (not used by default SQLite): **`docker-compose.yml`** at repo root.

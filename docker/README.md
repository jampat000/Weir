# MediaMop — Docker (alpha testers)

MediaMop is **Windows-first** for day-to-day development; this Docker image exists so **Linux/macOS/Windows** testers can run an **all-in-one** build (API + production web UI + SQLite) without the full Python/Node dev stack.

**Status:** **Alpha** — same codebase as `main`, but the container path is newer than the PowerShell + `npm run dev` workflow. Report issues with repro steps and logs.

---

## Run from GitHub Container Registry (GHCR)

**You do not need a GitHub account** to use the image if the container package is **public** (see [For repo maintainers](#for-repo-maintainers-anonymous-docker-pull) — otherwise use `docker login` as in [Pull a pre-built image](#pull-a-pre-built-image-github-container-registry)).

1. **Image must exist.** A maintainer runs **[Docker alpha](../.github/workflows/docker-alpha.yml)** (Actions tab → *Docker alpha* → *Run workflow*) or pushes a tag like `v0.1.0-alpha.1` so CI pushes `ghcr.io/jampat000/mediamop:TAG`.

2. **Pull and run** (replace the secret with a random string **≥ 32 characters**):

   ```bash
   docker pull ghcr.io/jampat000/mediamop:alpha
   docker run --rm \
     -e MEDIAMOP_SESSION_SECRET="replace-with-a-long-random-secret-at-least-32-chars" \
     -p 8788:8788 \
     -v mediamop-data:/data/mediamop \
     ghcr.io/jampat000/mediamop:alpha
   ```

3. Open **`http://localhost:8788/`** (first start on an empty volume runs **admin bootstrap**).

**Browse versions on GitHub:** when the package exists, it appears under the repository **Packages** entry, e.g. **[github.com/jampat000/mediamop/pkgs/container/mediamop](https://github.com/jampat000/mediamop/pkgs/container/mediamop)** (tags and digest are listed there).

**Compose (optional):** only if you **cloned this repo** (you need root **`compose.yaml`**). Same flow: **`.env.mediamop`** + [Default: Docker Compose + GHCR](#default-docker-compose--ghcr-repo-root). If you **did not** clone the repo, use **`docker pull`** + **`docker run`** above — no Compose files required.

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

## Default: Docker Compose + GHCR (repo root)

**Prerequisite:** A **git clone** of this repository at a revision that includes **`compose.yaml`** in the **root** of the clone (use current **`main`**). If someone sent you only a command and you have **no** repo folder, Compose will fail with “file not found” / “no configuration file” — use **`docker pull`** + **`docker run`** in [Run from GHCR](#run-from-github-container-registry-ghcr) instead.

The container uses **SQLite only** (bundled with the app). You do **not** set up PostgreSQL for normal Docker use.

**Working directory:** the **repository root** (the directory that contains **`compose.yaml`**).

1. Copy the example env file and set **`MEDIAMOP_SESSION_SECRET`** (32+ characters):

   ```bash
   cp docker/mediamop.env.example .env.mediamop
   ```

2. If the GHCR package is **private**, log in once:

   ```bash
   echo "YOUR_GITHUB_PAT" | docker login ghcr.io -u YOUR_GITHUB_USERNAME --password-stdin
   ```

3. Pull and start (**no** `-f` flag, **no** `--build`):

   ```bash
   docker compose --env-file .env.mediamop pull
   docker compose --env-file .env.mediamop up -d
   ```

4. Open **`http://localhost:8788/`**.

**Stop / remove container (keep data volume):**

```bash
docker compose --env-file .env.mediamop down
```

**Same stack, explicit file (back-compat):** `docker compose --env-file .env.mediamop -f docker-compose.mediamop.ghcr.yml pull` — that file **`include`s** `compose.yaml`.

---

## Recommended: Docker Compose (local build from Dockerfile)

**Working directory:** repository **root** (where **`docker-compose.mediamop.yml`** and **`compose.yaml`** live). Use **`-f docker-compose.mediamop.yml`** so Compose does not pick the default **`compose.yaml`** (GHCR) by mistake.

1. Copy the example env file:

   ```bash
   cp docker/mediamop.env.example .env.mediamop
   ```

2. Edit **`.env.mediamop`** and set **`MEDIAMOP_SESSION_SECRET`** to a long random value (32+ characters).

3. Start the stack (must pass **`-f`** so the local **build** file wins over default **`compose.yaml`**):

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

Maintainers publish images via **GitHub Actions → “Docker alpha”** (or tags like `v0.1.0-alpha.1`). Registry paths use **lowercase** `OWNER` and `REPO` (same as `github.repository` in Actions).

**Canonical example (this repo):**

```text
ghcr.io/jampat000/mediamop:alpha
```

**General shape:**

```text
ghcr.io/OWNER/REPO:TAG
```

**If the package is private**, authenticate before `docker pull`:

```bash
echo "YOUR_GITHUB_PAT" | docker login ghcr.io -u YOUR_GITHUB_USERNAME --password-stdin
docker pull ghcr.io/jampat000/mediamop:alpha
```

Then run with `docker run` (same `-e`, `-p`, `-v` as above) using that image reference instead of `mediamop:local`, or use **Docker Compose (pre-built from GHCR)** above.

**Public vs private:** New GHCR packages are often **private** (inherits from the repo or defaults that way). **Private** = every user must `docker login ghcr.io` with a PAT that has **`read:packages`**. **Public** = anyone can `docker pull` with no GitHub login — use this for frictionless testers.

---

## For repo maintainers: anonymous docker pull

After the first successful **Docker alpha** push:

1. Open the package page (from the repo: **Packages** in the right sidebar, or **[`github.com/jampat000/mediamop/pkgs/container/mediamop`](https://github.com/jampat000/mediamop/pkgs/container/mediamop)**).
2. **Package settings** (gear) → **Change package visibility** → **Public** → confirm.

Until you do this, tell testers to **`docker login ghcr.io`** (PAT with `read:packages`) before `docker pull`, or they will get **denied** / **unauthorized**.

---

## Ports and health

| Port | Service |
|------|---------|
| **8788** | HTTP — API (`/api/v1`, `/health`) and static UI (`/`) |

The image includes a **`HEALTHCHECK`** on **`GET /health`**. `docker ps` shows `healthy` once migrations finished and uvicorn is up.

---

## Troubleshooting

### `docker compose` / `docker: 'compose' is not a docker command`

Install **Docker Desktop** (Windows/macOS) or **Docker Engine** with the **Compose V2 plugin** (Linux). This repo uses **`docker compose`** (space), not the legacy standalone **`docker-compose`** (hyphen) binary. Verify: **`docker compose version`**.

### `can't find a suitable configuration file` / `compose.yaml` does not exist

You ran Compose **outside** the repo root, or you **never cloned** the repository. **`compose.yaml`** only exists **in this Git repo**. Either **`cd`** to the clone root where **`compose.yaml`** is present, or skip Compose and use **`docker pull`** + **`docker run`** in [Run from GHCR](#run-from-github-container-registry-ghcr).

### `manifest unknown` / `pull access denied` for `ghcr.io/...`

- **manifest unknown:** that tag was never pushed (or was deleted). Check **[Packages](https://github.com/jampat000/mediamop/pkgs/container/mediamop)** for available tags, or ask a maintainer to run **Docker alpha** on GitHub Actions.
- **denied / unauthorized:** the package is **private** — run **`docker login ghcr.io`** with a PAT that has **`read:packages`**, or ask the maintainer to set package visibility to **Public** ([maintainer steps](#for-repo-maintainers-anonymous-docker-pull)).

### Errors about `include` when using `-f docker-compose.mediamop.ghcr.yml`

Update to a **current Docker Desktop / Compose v2** release. That file uses Compose **`include`**, which needs a **recent Compose specification** implementation.

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

Repo root also has **`compose.yaml`** (default GHCR stack), **`Dockerfile`**, **`docker-compose.mediamop.yml`** (local build), **`docker-compose.mediamop.ghcr.yml`** (includes `compose.yaml`), and **`.github/workflows/docker-alpha.yml`** (CI build/push).

---

## More detail

Release policy (tags, zip artifacts): **[`docs/release.md`](../docs/release.md)**.

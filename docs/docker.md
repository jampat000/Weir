# Docker (all-in-one)

**Tester requirements, prerequisites, and step-by-step instructions** live in **[`docker/README.md`](../docker/README.md)**. Read that file first.

Short summary:

- One container: **FastAPI + SQLite + bundled web UI** on **port 8788**, same-origin **`/api/v1`**.
- **Alpha** path — primary release process remains **[release.md](release.md)** (git tag + `mediamop-web-dist.zip`).
- Compose: root **`compose.yaml`** (default `docker compose` — GHCR pull) or **`docker-compose.mediamop.yml`** (local build) + **`docker/mediamop.env.example`**. Requires a **repo clone**; otherwise use **`docker pull`** / **`docker run`** from **`docker/README.md`**.
- CI image push: **GitHub Actions → Docker alpha** (`.github/workflows/docker-alpha.yml`).

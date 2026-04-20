# Docker (all-in-one)

**Tester requirements, prerequisites, and step-by-step instructions** live in **[`docker/README.md`](../docker/README.md)**. Read that file first.

Short summary:

- One container: **FastAPI + SQLite + bundled web UI** on **port 8788**, same-origin **`/api/v1`**.
- **Alpha** path — primary release process remains **[release.md](release.md)** (git tag + `mediamop-web-dist.zip`).
- Compose: **`docker-compose.mediamop.yml`** at repo root + **`docker/mediamop.env.example`**.
- CI image push: **GitHub Actions → Docker alpha** (`.github/workflows/docker-alpha.yml`).

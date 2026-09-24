# Weir server (.NET)

Weir's server is C# on .NET 10. It serves the HTTP API under `/api/v1` and the built web app, runs the durable job queue and the Processing and library-mode work, and owns the SQLite database. The Docker image and the Windows package both run it. Why the server is .NET: [ADR-0017](../../docs/adr/ADR-0017-backend-on-dotnet.md).

## Projects

Solution: `apps/server/Weir.slnx`.

| Project | Holds |
| --- | --- |
| `src/Weir.Host` | The process: configuration, logging, database startup, Kestrel, Windows Service and systemd support. Builds `Weir` / `Weir.exe`. |
| `src/Weir.Api` | Endpoints and HTTP behaviour: auth and CSRF, security headers, request ids, the OpenAPI document, serving the web app. |
| `src/Weir.Infrastructure` | SQLite (connections, stores, numbered migrations in `Migrations/`), the job queue and workers, the remux pass, library mode, the ffmpeg and mkvmerge runners, media manager clients, notifications, the filesystem and log files. |
| `src/Weir.Core` | Records and rules with no IO: the rules engine, job rules, media manager rules, configuration, settings and security primitives. |
| `tests/Weir.Core.Tests` | Unit tests for `Weir.Core`, including the rules and ffmpeg golden fixtures. |
| `tests/Weir.Infrastructure.Tests` | Tests against a real SQLite database and real files, including migration tests. |
| `tests/Weir.Api.Tests` | Endpoint tests over HTTP, including `OpenApiDocumentParityTests`. |

The language-neutral contract suite (`tests/contract`) and the E2E smoke (`tests/e2e/weir`) judge a running server from outside. See [`tests/contract/README.md`](../../tests/contract/README.md).

## Build and test

Needs the .NET SDK pinned in `apps/server/global.json` (10.0.400, no roll-forward). From the repository root:

```powershell
dotnet build apps/server/Weir.slnx -warnaserror
dotnet test apps/server/Weir.slnx
```

`RealFfmpegTests` run real ffprobe and ffmpeg and skip unless the tools are found through `WEIR_FFMPEG_DIR` or `PATH`.

## Run

```powershell
$env:WEIR_HOME = "$env:TEMP\weir-dev"          # optional; defaults to %PROGRAMDATA%\Weir
$env:WEIR_WEB_DIST = "$PWD\apps\web\dist"      # optional; after `npm run build` in apps/web
dotnet run --project apps/server/src/Weir.Host -- --host 127.0.0.1 --port 9347
```

`--port` (what the Windows tray passes) wins over `PORT` (what the Docker entrypoint sets). Both default to 9347, and the server binds every interface unless `--host` narrows it. `Weir --version` prints the version and exits.

For day-to-day work, `scripts/dev-backend.ps1` loads the repository-root `.env`, picks the dev ports and starts the server. See [`docs/local-development.md`](../../docs/local-development.md).

## Configuration

The server reads `WEIR_*` environment variables and nothing else. It does not read a `.env` file itself. `WeirOptionsLoader.Load` (in `Weir.Core/Configuration`) turns the environment into one immutable `WeirOptions` record at startup; `RuntimePaths` resolves `WEIR_HOME`, the database path (`WEIR_DB_PATH`, default `data/weir.sqlite3` under `WEIR_HOME`) and the other runtime folders. An invalid value (for example `WEIR_CORS_ORIGINS=*`, a malformed octal mode or a bad port) stops startup with a message instead of being ignored.

The variables and their defaults are documented in the repository's [`.env.example`](../../.env.example). Operator settings that can change while Weir runs (libraries, rules, schedules, cleanup intervals, alerts) live in the database and are edited in the app under Settings, not through the environment.

## Schema and migrations

The numbered SQL scripts in `src/Weir.Infrastructure/Migrations/` are the only source of schema changes. `Weir.Infrastructure.Sqlite.SchemaMigrator` lists them in order, each with the revision it leaves behind, and records the current revision in the `alembic_version` table. The table name is kept from the retired Python backend, so databases from earlier releases are recognised and upgraded.

The current head is migration `0015_handback_outcomes`, revision `0050_handback_outcomes`. The first script, `0001_baseline_0036_drop_pruner_tables.sql`, is the frozen baseline (revision `0036_drop_pruner_tables`).

On startup the server:

- creates a missing database at head;
- opens a database already at head without writing to it;
- upgrades a database at any earlier revision in the list by applying every later migration, in order, in one transaction;
- refuses anything else (an unknown revision, usually from a newer release, or a pre-baseline revision) with a message, and changes nothing.

To change the schema, add the next numbered script (`0016_…sql`; the project embeds every `Migrations/*.sql`), append it to `SchemaMigrator.Migrations` with the next revision (`0051_…`), and add a migration test under `tests/Weir.Infrastructure.Tests/Sqlite/Migrations/` that builds a database at the previous head, writes a row in the old shape, upgrades and reads it back through the real store. `SchemaParityTests` stays pinned to the baseline and compares it against the frozen reference in `tests/Weir.Infrastructure.Tests/schema/alembic-head.sql`, which is never edited.

## API contract

`apps/web/openapi/weir-openapi.json` is the committed API contract, maintained by hand. The server embeds it at build time and serves it at `/openapi.json`, pruned to the operations it maps. When an endpoint changes, edit that file in the same change, run `npm run api:types:generate` in `apps/web` to regenerate the TypeScript types, and keep `OpenApiDocumentParityTests` passing. `npm run api:types:check` is the CI gate that the two agree.

## Publish and packaging

Self-contained single-file builds for `win-x64`, `linux-x64` and `linux-arm64` use the checked-in profiles in `src/Weir.Host/Properties/PublishProfiles`:

```powershell
dotnet publish apps/server/src/Weir.Host -p:PublishProfile=win-x64       # apps/server/artifacts/publish/win-x64/Weir.exe
dotnet publish apps/server/src/Weir.Host -p:PublishProfile=linux-arm64
```

Always publish through a profile. Passing `-r`, `--self-contained` or `-p:PublishSingleFile=true` on the command line makes those global properties, and the single-file analyzer then fails `Weir.Infrastructure` with IL3000. The profiles scope them to `Weir.Host`.

`MediaToolResolver` looks for ffmpeg and ffprobe in `WEIR_FFMPEG_DIR`, then (in a single-file build) `<app>/bin/ffmpeg`, then `PATH` (`.exe` only on Windows; PATHEXT and the current directory are never consulted). mkvmerge is found the same way (`WEIR_MKVTOOLNIX_DIR`, `<app>/bin/mkvtoolnix`, `PATH`) but is optional: without it a library's writer setting falls back to ffmpeg. `WEIR_HOME` is never searched — a default Windows install lets every local account write to it, so a tool found there could never be trusted. `GET /api/v1/system/media-tools` reports what an install has.

The product version is `WeirVersion` in `apps/server/Directory.Build.props`. The assembly version, `--version`, `/openapi.json` and both packages take it from there.

- **Docker** (`Dockerfile`, `docker/entrypoint.sh`, [`docker/README.md`](../../docker/README.md)): the SDK stage cross-publishes `linux-x64` or `linux-arm64` on the build machine's architecture; the final stage is `mcr.microsoft.com/dotnet/runtime-deps:10.0-noble` with ffmpeg and mkvtoolnix from the distribution, user `weir` (UID/GID 1000), data under `/data/weir`, port 9347 and a `/health` healthcheck. The entrypoint has no migration step; the server migrates its own database.
- **Windows** (`packaging/windows/build-velopack.ps1`): builds the web app, publishes the server with the `win-x64` profile, publishes the tray app and packs them with Velopack. The tray is `Weir.exe` at the root; the server is `server/WeirServer.exe`, next to `server/web-dist`, `server/bin/ffmpeg` and `server/bin/mkvtoolnix`. `scripts/smoke-windows-package.ps1` then starts the packaged server the way the tray does and runs a real pass-through job.

## History

The notes kept while the server was ported from the Python backend, and the per-issue design notes that followed, are in [`docs/archive/server-port-notes.md`](../../docs/archive/server-port-notes.md). They are a historical record, not current documentation. Some code comments and test names still name the Python function a piece of code was ported from; the Python code is in git history before #523.

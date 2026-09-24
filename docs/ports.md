# Weir — ports (canonical)

## Installed Weir

Weir listens on **9347** by default, on every interface
(`ServerListenOptions.DefaultPort` in `apps/server/src/Weir.Core/Configuration/ServerListenOptions.cs`).
The web app and the API share that one port: the API is `/api/v1` on the same origin.

| Install | How the port is chosen | Change it |
|---------|------------------------|-----------|
| Windows (tray) | The tray starts the server with `--port`. It uses 9347, or asks for another port when 9347 is taken. Without a desktop to ask, it takes the first free port above 9347. | Tray menu › `Change port` |
| Docker | The entrypoint passes `PORT`, default 9347. The image `EXPOSE`s 9347 and its health check calls `/health` on that port. | Set `PORT`, or map a different host port (`-p 8080:9347`) |

Behind a reverse proxy, clients use the proxy's normal HTTPS port (443) and the proxy forwards to
Weir's port.

## Development (local machine)

**Single source of numeric defaults:** [`scripts/dev-ports.json`](../scripts/dev-ports.json)  
Vite reads it from [`apps/web/vite.config.ts`](../apps/web/vite.config.ts). The dev scripts read the same file.

| Role | Host | Port | URL example |
|------|------|------|-------------|
| Web app (Vite **dev** and **preview**) | `127.0.0.1` (local only) | **8782** | `http://127.0.0.1:8782` |
| API (the .NET server, `dotnet watch run` via `npm run dev` or `scripts/dev-backend.ps1`) | `127.0.0.1` | **18788** | `http://127.0.0.1:18788` |

The browser should use the **web** URL. `/api` is proxied to the API origin above (same-origin cookies).

**The dev API port is deliberately not the installed default.** A machine that also has Weir
installed must never have `npm run dev`'s port collide with it. `npm run dev:stop-api` also stops
the dev API only by the PID it recorded when it started that process (`.dev-api.pid` at the repo
root, read by `scripts/stop-dev-api-port.mjs`), never by scanning the port, so an installed instance
is never at risk.

If a port is busy when `npm run dev` starts, `apps/web/scripts/run-dev-stack.mjs` moves to the next
free port and prints the URL it used. `npm run dev:stop-web` stops the dev web server the same way
`dev:stop-api` stops the dev API: only by the PID it recorded when it started that process
(`.dev-web.pid` at the repo root, read by `scripts/stop-dev-web-port.mjs`), never by scanning the port.

**Windows / `ERR_CONNECTION_REFUSED`:** Vite binds only `127.0.0.1` (IPv4), so use
`http://127.0.0.1:8782`, not `http://localhost:8782` — on Windows `localhost` can resolve to `::1`
(IPv6), which nothing is listening on.

**Overrides (temporary):**

- API port: `WEIR_DEV_API_PORT` when running `dev-backend.ps1`.
- Vite proxy target: `VITE_DEV_API_PROXY_TARGET` (must match wherever the .NET server listens).
- Web dev/preview host: `VITE_HOST`, or pass `--host` to Vite, to expose it beyond localhost on
  purpose (for example to test from a phone on the same network).

**Changing defaults:** edit `scripts/dev-ports.json` and restart dev servers.

`dev-ports.json` also records `production.containerApiBindPort` (9347) and `production.publicHttpsPort`
(443) for reference; the server's own default is in code, as above.

## Database

The server uses **file-backed SQLite** under **`WEIR_HOME`**. There is no database port.

## CI / E2E

Automated tests pick **ephemeral loopback ports** (see `tests/e2e/weir/conftest.py`) so they do not depend on 8782/18788 being free.

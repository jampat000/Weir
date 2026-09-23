#!/usr/bin/env node
/**
 * Stops **this worktree's** dev web server (Vite) and nothing else.
 *
 * Like stop-dev-api-port.mjs, it identifies the exact process before touching it, and never stops
 * something merely because it holds the port: another worktree's Vite, or any other program, can be
 * listening on the same number. The process `npm run dev` starts for the web app runs this worktree's
 * `apps/web/node_modules/vite/bin/vite.js` (apps/web/scripts/run-dev-stack.mjs), so a listener on the dev
 * web port is stopped only when its command line names that file.
 *
 * Use when `npm run dev` fails with "Port 8782 is already in use" (a leftover Vite from this worktree).
 * The port comes from scripts/dev-ports.json, or WEIR_DEV_WEB_PORT.
 */
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { platform } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const viteEntry = path.join(repoRoot, "apps", "web", "node_modules", "vite", "bin", "vite.js");
const isWindows = platform() === "win32";

function readWebPort() {
  const forced = (process.env.WEIR_DEV_WEB_PORT || "").trim();
  const raw = forced || JSON.parse(readFileSync(path.join(repoRoot, "scripts", "dev-ports.json"), "utf8")).development.webPort;
  const port = Number(raw);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error(`Not a port: ${raw}`);
  }
  return port;
}

function output(command, args) {
  try {
    return execFileSync(command, args, { encoding: "utf8", windowsHide: true, stdio: ["ignore", "pipe", "ignore"] });
  } catch {
    // No listener (lsof exits 1) or the tool is missing: either way, nothing identified.
    return "";
  }
}

function powershell(script) {
  return output("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script]);
}

/** Process ids listening on the port. */
function listeners(port) {
  const out = isWindows
    ? powershell(`Get-NetTCPConnection -LocalPort ${port} -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique`)
    : output("lsof", ["-i", `TCP:${port}`, "-sTCP:LISTEN", "-t"]);
  return [...new Set(out.split(/\r?\n/).map((line) => line.trim()).filter((line) => /^\d+$/.test(line)))].map(Number);
}

/** The full command line of a running process, or "" when it is gone or unreadable. */
function commandLine(pid) {
  return isWindows
    ? powershell(`(Get-CimInstance Win32_Process -Filter "ProcessId=${pid}" -ErrorAction SilentlyContinue).CommandLine`).trim()
    : output("ps", ["-p", String(pid), "-o", "command="]).trim();
}

/** Compares paths the way the platform does: case-insensitive with either slash on Windows. */
function mentions(line, file) {
  if (!isWindows) return line.includes(file);
  const normalize = (text) => text.replaceAll("/", "\\").toLowerCase();
  return normalize(line).includes(normalize(file));
}

function stop(pid) {
  try {
    if (isWindows) {
      execFileSync("taskkill", ["/T", "/F", "/PID", String(pid)], { stdio: "inherit" });
    } else {
      process.kill(pid, "SIGTERM");
    }
    return true;
  } catch {
    return false;
  }
}

const port = readWebPort();
const pids = listeners(port);
if (pids.length === 0) {
  console.error(`[stop-dev-web-port] Nothing is listening on port ${port}.`);
  process.exit(0);
}

for (const pid of pids) {
  const line = commandLine(pid);
  if (!line) {
    console.error(`[stop-dev-web-port] PID ${pid} on port ${port} is gone or its command line cannot be read; leaving it.`);
    continue;
  }
  if (!mentions(line, viteEntry)) {
    console.error(
      `[stop-dev-web-port] PID ${pid} on port ${port} is not this worktree's dev web server (its command line does not ` +
        `run ${viteEntry}); leaving it alone. Stop it yourself, or set WEIR_DEV_WEB_PORT to use another port.`,
    );
    continue;
  }
  console.error(`[stop-dev-web-port] Stopping this worktree's dev web server (PID ${pid}, port ${port}).`);
  if (!stop(pid)) {
    console.error(`[stop-dev-web-port] Could not stop PID ${pid}; close the terminal running Vite, or use an elevated shell.`);
  }
}

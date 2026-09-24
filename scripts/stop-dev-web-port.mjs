#!/usr/bin/env node
/**
 * Stops **this worktree's** dev web server (Vite) — the one
 * `apps/web/scripts/run-dev-stack.mjs` spawned and recorded in `.dev-web.pid` at the repo root —
 * and nothing else.
 *
 * Like stop-dev-api-port.mjs, it identifies the exact process before touching it, and never stops
 * whatever happens to be *listening on the dev web port*: another worktree's Vite, an installed
 * Weir's static file server, or any other program can be listening on that same number.
 *
 * Use when `npm run dev` fails with "Port 8782 is already in use" (a leftover Vite from this
 * worktree), or the port is otherwise stuck. Then run `npm run dev` again from `apps/web`.
 */
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, rmSync } from "node:fs";
import { platform } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const pidFilePath = path.join(repoRoot, ".dev-web.pid");
const isWindows = platform() === "win32";

function removePidFile() {
  try {
    rmSync(pidFilePath, { force: true });
  } catch {
    /* ignore — best-effort cleanup */
  }
}

function readRecordedProcess() {
  if (!existsSync(pidFilePath)) {
    return null;
  }
  try {
    const raw = JSON.parse(readFileSync(pidFilePath, "utf8"));
    const pid = Number(raw?.pid);
    const viteEntry = String(raw?.viteEntry || "");
    if (!Number.isInteger(pid) || pid <= 0 || !viteEntry) {
      return null;
    }
    return { pid, viteEntry };
  } catch {
    return null;
  }
}

function powershell(script) {
  try {
    return execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], {
      encoding: "utf8",
      windowsHide: true,
    });
  } catch {
    return "";
  }
}

/**
 * The full command line of a running process, or "" when it is gone or unreadable. On Windows,
 * `Get-CimInstance Win32_Process` is what lets us tell "this PID is Node running *our* Vite
 * entrypoint" apart from "this PID happens to exist and answers to the same number" (PIDs are
 * recycled by the OS soon after a process exits, so the bare fact that some process has this PID
 * proves nothing on its own).
 */
function commandLine(pid) {
  if (isWindows) {
    return powershell(
      `(Get-CimInstance Win32_Process -Filter "ProcessId=${pid}" -ErrorAction SilentlyContinue).CommandLine`,
    ).trim();
  }
  try {
    return execFileSync("ps", ["-p", String(pid), "-o", "command="], { encoding: "utf8" }).trim();
  } catch {
    return "";
  }
}

/** Compares paths the way the platform does: case-insensitive with either slash on Windows. */
function mentions(line, file) {
  if (!isWindows) return line.includes(file);
  const normalize = (text) => text.replaceAll("/", "\\").toLowerCase();
  return normalize(line).includes(normalize(file));
}

function stopProcessTree(pid) {
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

const recorded = readRecordedProcess();

if (!recorded) {
  console.error(
    "[stop-dev-web-port] No recorded dev web process for this worktree (.dev-web.pid missing " +
      "or unreadable) — nothing to stop. This only stops a process `npm run dev` itself started; " +
      "it never scans the port for arbitrary listeners (that could be an installed Weir or " +
      "another worktree's Vite).",
  );
  process.exit(0);
}

const line = commandLine(recorded.pid);

if (!line) {
  console.error(
    `[stop-dev-web-port] Recorded PID ${recorded.pid} is not running (already stopped, or the ` +
      "machine restarted) — clearing the stale record.",
  );
  removePidFile();
  process.exit(0);
}

if (!mentions(line, recorded.viteEntry)) {
  console.error(
    `[stop-dev-web-port] PID ${recorded.pid} is running but is not this worktree's dev web server ` +
      `— its command line does not reference ${recorded.viteEntry}. The OS likely reused the PID ` +
      "for an unrelated process after the dev web server exited. Leaving it alone: not knowing is " +
      "not permission. Clearing the stale record.",
  );
  removePidFile();
  process.exit(0);
}

console.error(`[stop-dev-web-port] Stopping this worktree's dev web server (PID ${recorded.pid}).`);
if (!stopProcessTree(recorded.pid)) {
  console.error(
    `[stop-dev-web-port] Could not stop PID ${recorded.pid} — close the terminal running Vite, ` +
      "or run this from an elevated shell.",
  );
}
removePidFile();

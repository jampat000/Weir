#!/usr/bin/env node
/**
 * Starts the MediaMop API and Vite together without `node_modules/.bin`
 * (some Windows installs lack a working `.bin` on PATH when npm runs scripts via `cmd.exe`).
 */
import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const webDir = path.join(__dirname, "..");
const apiScript = path.join(__dirname, "run-api-dev.mjs");
const viteEntry = path.join(webDir, "node_modules", "vite", "bin", "vite.js");

if (!existsSync(viteEntry)) {
  console.error(`Missing ${viteEntry}. Run npm install (or npm ci) in apps/web.`);
  process.exit(1);
}

const node = process.execPath;

const api = spawn(node, [apiScript], {
  cwd: webDir,
  stdio: "inherit",
  env: { ...process.env },
});

const web = spawn(node, [viteEntry], {
  cwd: webDir,
  stdio: "inherit",
  env: { ...process.env },
});

let stopping = false;

function wireExit(name, child, other) {
  child.on("exit", (code, signal) => {
    if (stopping) {
      return;
    }
    stopping = true;
    try {
      if (!other.killed) {
        other.kill("SIGTERM");
      }
    } catch {
      /* ignore */
    }
    if (code !== 0 && code !== null) {
      console.error(`[${name}] exited with code ${code}${signal ? ` (${signal})` : ""}.`);
    }
    if (signal) {
      process.exit(1);
    }
    process.exit(code ?? 0);
  });
}

process.on("SIGINT", () => {
  try {
    api.kill("SIGINT");
  } catch {
    /* ignore */
  }
  try {
    web.kill("SIGINT");
  } catch {
    /* ignore */
  }
});

process.on("SIGTERM", () => {
  try {
    api.kill("SIGTERM");
  } catch {
    /* ignore */
  }
  try {
    web.kill("SIGTERM");
  } catch {
    /* ignore */
  }
});

wireExit("api", api, web);
wireExit("web", web, api);

#!/usr/bin/env node
// Local pre-push checks: the fast CI gates that fail most often on formatting and sync, run before a push
// rather than after a CI run. .githooks/pre-push calls this (one-time setup: git config core.hooksPath .githooks);
// it can also be run by hand: node scripts/pre-push.mjs
//
// A check whose tool is not installed yet (ruff, or apps/web/node_modules) is skipped with the command that
// installs it, so a fresh clone can still push.
import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const webDir = path.join(repoRoot, "apps", "web");
const prettier = path.join(webDir, "node_modules", "prettier", "bin", "prettier.cjs");
const generatedTypes = "apps/web/src/lib/api/generated/openapi-types.ts";

function run(command, args, options = {}) {
  return spawnSync(command, args, { cwd: repoRoot, stdio: "inherit", ...options }).status === 0;
}

function quiet(command, args) {
  return spawnSync(command, args, { cwd: repoRoot, stdio: "ignore" }).status === 0;
}

function fail(message, fix) {
  console.error(`[pre-push] FAIL: ${message}`);
  if (fix) console.error(`  Fix: ${fix}`);
  process.exit(1);
}

const skip = (label, install) => console.log(`[pre-push] ${label} skipped (install: ${install})`);
const step = (label) => console.log(`[pre-push] ${label}...`);

// ruff: the contract suite's lint and format.
if (!quiet("ruff", ["--version"])) {
  skip("ruff", "python -m pip install --require-hashes -r tests/requirements.txt");
} else {
  step("ruff");
  if (!run("ruff", ["check", "tests/contract"])) fail("ruff check failed", "ruff check --fix tests/contract");
  if (!run("ruff", ["format", "--check", "tests/contract"])) fail("ruff format --check failed", "ruff format tests/contract");
}

if (!existsSync(prettier)) {
  skip("prettier, dead-code guard and API types drift", "npm ci in apps/web");
} else {
  step("prettier");
  const files = ["src/**/*.{ts,tsx,css}", "index.html", "vite.config.ts"];
  if (!run(process.execPath, [prettier, "--check", ...files], { cwd: webDir })) {
    fail("prettier check failed", "npm run format:fix in apps/web");
  }

  step("dead-code guard");
  if (!run(process.execPath, [path.join(repoRoot, "scripts", "check-dead-code.mjs")])) fail("dead-code guard failed");

  // The committed OpenAPI document is the API contract the server embeds; the web app's generated types must
  // match it. Regenerating overwrites the file, so this only runs on a clean copy and puts it back afterwards.
  step("API types drift");
  if (!quiet("git", ["diff", "--quiet", "--", generatedTypes])) {
    fail(`${generatedTypes} has uncommitted changes`, "commit or discard them, then push again");
  }
  // npm is a .cmd on Windows, which Node only starts through a shell; the command is fixed text.
  if (!run("npm run api:types:generate", [], { cwd: webDir, shell: true })) fail("npm run api:types:generate failed");
  if (!quiet("git", ["diff", "--quiet", "--", generatedTypes])) {
    run("git", ["--no-pager", "diff", "--", generatedTypes]);
    run("git", ["restore", "--", generatedTypes]);
    fail(
      "the generated API types do not match apps/web/openapi/weir-openapi.json",
      "npm run api:types:generate in apps/web, then commit the result",
    );
  }
}

console.log("[pre-push] All checks passed.");

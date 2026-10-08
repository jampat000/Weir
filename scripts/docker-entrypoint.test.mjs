// node --test scripts/docker-entrypoint.test.mjs
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

const entrypoint = readFileSync(join(dirname(fileURLToPath(import.meta.url)), "..", "docker", "entrypoint.sh"), "utf8");
const hasShell = spawnSync("sh", ["-c", "umask"]).status === 0;
const skip = hasShell ? false : "no POSIX shell on this machine";

const posix = (path) => path.replaceAll("\\", "/");

// Runs the entrypoint as a non-root user with a stand-in server that reports the umask and credentials secret it
// was started with. Returns what the stand-in saw, and the shell's own umask for comparison.
function startServer(home, env) {
  const root = mkdtempSync(join(tmpdir(), "weir-entrypoint-"));
  try {
    const app = join(root, "app");
    mkdirSync(app);
    writeFileSync(join(app, "Weir"), '#!/bin/sh\nprintf "%s\\n%s\\n" "$(umask)" "$WEIR_CREDENTIALS_SECRET"\n', { mode: 0o755 });
    const script = join(root, "entrypoint.sh");
    writeFileSync(script, entrypoint.replaceAll("/opt/weir", posix(app)));
    const environment = { ...process.env, WEIR_HOME: posix(home), WEIR_PUID: "1000", WEIR_PGID: "1000", ...env };
    delete environment.WEIR_CREDENTIALS_SECRET;
    Object.assign(environment, env);
    const run = spawnSync("sh", [posix(script)], { env: environment, encoding: "utf8" });
    assert.equal(run.status, 0, run.stderr);
    const [umask, credentials] = run.stdout.split("\n");
    return { umask, credentials, shellUmask: spawnSync("sh", ["-c", "umask"], { encoding: "utf8" }).stdout.trim() };
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

for (const [name, env] of [
  ["a start that makes both secrets", {}],
  ["a start that supplies the session secret", { WEIR_SESSION_SECRET: "s".repeat(40) }],
]) {
  test(`the server keeps the caller's umask on ${name}`, { skip }, () => {
    const home = mkdtempSync(join(tmpdir(), "weir-home-"));
    try {
      const { umask, credentials, shellUmask } = startServer(home, env);
      assert.equal(umask, shellUmask, "owner-only secrets must not leave the server with a private umask");
      assert.ok(credentials.length >= 32, "the credentials secret reaches the server");
    } finally {
      rmSync(home, { recursive: true, force: true });
    }
  });
}

test("the credentials secret made on the first start is the one used on the next", { skip }, () => {
  const home = mkdtempSync(join(tmpdir(), "weir-home-"));
  try {
    const first = startServer(home, {});
    const second = startServer(home, {});
    assert.equal(second.credentials, first.credentials);
    assert.equal(readFileSync(join(home, "credentials.secret"), "utf8"), `${first.credentials}\n`);
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test("a credentials secret from the environment wins and no file is made", { skip }, () => {
  const home = mkdtempSync(join(tmpdir(), "weir-home-"));
  try {
    const { credentials } = startServer(home, { WEIR_CREDENTIALS_SECRET: "c".repeat(40) });
    assert.equal(credentials, "c".repeat(40));
    assert.throws(() => readFileSync(join(home, "credentials.secret")), { code: "ENOENT" });
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

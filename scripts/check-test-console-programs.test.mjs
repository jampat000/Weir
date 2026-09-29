// node --test scripts/check-test-console-programs.test.mjs
import assert from "node:assert/strict";
import { test } from "node:test";

import { findConsoleStandIns, isTestSource } from "./check-test-console-programs.mjs";

const flagged = [
  ['Path.Combine(Environment.SystemDirectory, "PING.EXE")', "the system ping copied as a stand-in"],
  ['new ProcessStartInfo("ping", "-n 120 127.0.0.1")', "ping started by name"],
  ['Argv = ["cmd.exe", "/d", "/c", "ping -n 5 127.0.0.1"]', "ping run through cmd"],
  ['Argv = ["cmd.exe", "/c", "timeout /t 30"]', "timeout run through cmd"],
  ['var exe = @"C:\\Windows\\System32\\PING.EXE";', "a verbatim path to ping"],
  ['var exe = "C:\\\\Windows\\\\System32\\\\ping.exe";', "an escaped path to ping"],
  ['Start("timeout.exe", "/t 30")', "timeout.exe by name"],
];

for (const [source, why] of flagged) {
  test(`${why} is refused`, () => {
    assert.equal(findConsoleStandIns(source).length, 1);
  });
}

const allowed = [
  ['using var ping = await admin.GetAsync("/api/v1/auth/admin/ping");', "an HTTP path ending in /ping"],
  ['run.TryGetProperty("timeout", out _)', "a JSON property called timeout"],
  ["// ping is what this used to start", "a comment naming ping"],
  ['/* "ping" */ var x = 1;', "a block comment naming ping"],
  ['var quote = \'"\'; var name = "Weir.TestChild";', "a character literal holding a quote"],
  ['throw new XunitException("A choice is saved.");', "prose that contains the word choice"],
];

for (const [source, why] of allowed) {
  test(`${why} is allowed`, () => {
    assert.deepEqual(findConsoleStandIns(source), []);
  });
}

test("a finding names the line it is on", () => {
  const [finding] = findConsoleStandIns('var a = 1;\nvar b = 2;\nvar exe = "ping";\n');
  assert.equal(finding.line, 3);
});

test("test projects are checked and product code is not", () => {
  assert.equal(isTestSource("apps/tray/Weir.Tray.Tests/InstallProcessesTests.cs"), true);
  assert.equal(isTestSource("apps/server/tests/Weir.Infrastructure.Tests/Processes/ProcessRunnerTests.cs"), true);
  assert.equal(isTestSource("apps/tray/Weir.Tray.StandInServer/Program.cs"), true);
  assert.equal(isTestSource("apps/server/src/Weir.Host/Program.cs"), false);
  assert.equal(isTestSource("apps/web/src/main.tsx"), false);
});

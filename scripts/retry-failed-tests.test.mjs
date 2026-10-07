import assert from "node:assert/strict";
import test from "node:test";

import { filterFor, judge, readOutcomes, report } from "./retry-failed-tests.mjs";

const trx = (rows) => `<TestRun><Results>${rows
  .map(([id, outcome]) => `<UnitTestResult testId="${id}" testName="ignored(1)" outcome="${outcome}" />`)
  .join("")}</Results><TestDefinitions>
  <UnitTest name="a" id="a"><Execution id="x" /><TestMethod codeBase="x.dll" className="Weir.E2E.Tests.LoginTests" name="Signs_in" /></UnitTest>
  <UnitTest name="b" id="b"><Execution id="y" /><TestMethod codeBase="x.dll" className="Weir.E2E.Tests.NavTests" name="Opens_every_page" /></UnitTest>
</TestDefinitions></TestRun>`;

test("readOutcomes names each result by its test method", () => {
  assert.deepEqual(readOutcomes(trx([["a", "Failed"], ["b", "Passed"], ["a", "Passed"]])), [
    { method: "Weir.E2E.Tests.LoginTests.Signs_in", outcome: "Failed" },
    { method: "Weir.E2E.Tests.NavTests.Opens_every_page", outcome: "Passed" },
    { method: "Weir.E2E.Tests.LoginTests.Signs_in", outcome: "Passed" },
  ]);
});

test("filterFor matches methods exactly and escapes the filter's own syntax", () => {
  assert.equal(filterFor(["A.B.One", "A.B.Two"]), "FullyQualifiedName=A.B.One|FullyQualifiedName=A.B.Two");
  assert.equal(filterFor(["A.B.One(x)"]), "FullyQualifiedName=A.B.One\(x\)");
});

test("a test that passed on the retry is flaky, one that failed again or was not run is failed", () => {
  const retried = readOutcomes(trx([["a", "Passed"]]));
  assert.deepEqual(judge(["Weir.E2E.Tests.LoginTests.Signs_in", "Weir.E2E.Tests.NavTests.Opens_every_page"], retried), {
    flaky: ["Weir.E2E.Tests.LoginTests.Signs_in"],
    failed: ["Weir.E2E.Tests.NavTests.Opens_every_page"],
  });
});

test("one failing row of a theory fails the method even when another row passed", () => {
  const retried = readOutcomes(trx([["a", "Passed"], ["a", "Failed"]]));
  assert.deepEqual(judge(["Weir.E2E.Tests.LoginTests.Signs_in"], retried), { flaky: [], failed: ["Weir.E2E.Tests.LoginTests.Signs_in"] });
});

test("the report lists flaky tests under the standard's heading", () => {
  const text = report({ flaky: ["A.B.One"], failed: ["A.B.Two"] }, "Browser tests, retry");
  assert.match(text, /Flaky \(passed on retry\):\n\n- A\.B\.One/);
  assert.match(text, /Failed twice:\n\n- A\.B\.Two/);
});

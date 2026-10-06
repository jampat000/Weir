// node --test scripts/check-no-python.test.mjs
import assert from "node:assert/strict";
import { test } from "node:test";

import { pythonFiles } from "./check-no-python.mjs";

test("Python source and stub files are found wherever they sit", () => {
  assert.deepEqual(pythonFiles(["scripts/tool.py", "tests/x/test_a.py", "a/b.pyi", "gui.pyw", "ext.pyx", "SCRIPT.PY"]), [
    "scripts/tool.py",
    "tests/x/test_a.py",
    "a/b.pyi",
    "gui.pyw",
    "ext.pyx",
    "SCRIPT.PY",
  ]);
});

test("other files are left alone, including ones with py in the name", () => {
  assert.deepEqual(pythonFiles(["scripts/check-no-python.mjs", "docs/happy.md", "apps/web/copy.ts", "pyproject.toml", "a/py"]), []);
});

test("an empty repository has none", () => {
  assert.deepEqual(pythonFiles([]), []);
});

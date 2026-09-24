import { expect, it } from "vitest";

import { baseName } from "./path";

it("reads the last part of a Windows or POSIX path", () => {
  expect(baseName("C:\\Media\\Films\\A.Film.2020.mkv")).toBe("A.Film.2020.mkv");
  expect(baseName("tv/Show/Show.S01E01.mkv")).toBe("Show.S01E01.mkv");
});

it("ignores a trailing separator and keeps a bare name as it is", () => {
  expect(baseName("tv/Show/")).toBe("Show");
  expect(baseName("Show.S01E01.mkv")).toBe("Show.S01E01.mkv");
  expect(baseName("")).toBe("");
});

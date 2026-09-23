import { render } from "@testing-library/react";
import { expect, it } from "vitest";

import { FileName, baseName } from "./file-name";

it("shows the whole file name, with a break offered between its parts and none before the extension", () => {
  const { container } = render(
    <FileName path="The.Long.Tide.2024.REPACK2-WEIRSIM/The.Long.Tide.2024.2160p.REPACK2-WEIRSIM.mkv" />,
  );
  const name = container.querySelector("[data-file-name]")!;

  // Nothing is cut: the text is the file's full name, REPACK2 included.
  expect(name.textContent).toBe("The.Long.Tide.2024.2160p.REPACK2-WEIRSIM.mkv");
  // A break after each dot and dash of the stem, so it wraps between parts rather than mid-word.
  expect(name.querySelectorAll("wbr")).toHaveLength(6);
  expect(name.innerHTML.endsWith("WEIRSIM.mkv")).toBe(true);
});

it("reads the last part of a Windows or POSIX path", () => {
  expect(baseName("C:\\Media\\Films\\A.Film.2020.mkv")).toBe("A.Film.2020.mkv");
  expect(baseName("tv/Show/Show.S01E01.mkv")).toBe("Show.S01E01.mkv");
});

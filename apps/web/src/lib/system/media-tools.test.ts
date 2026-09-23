import { describe, expect, it } from "vitest";
import { toolVersion } from "./media-tools";

describe("toolVersion", () => {
  it("reads the version out of each tool's banner", () => {
    expect(
      toolVersion("ffmpeg version 7.1-full_build-www.gyan.dev Copyright"),
    ).toBe("7.1");
    expect(toolVersion("ffmpeg version n9.0.1-9-gfa97c9f046-20260101")).toBe(
      "9.0.1",
    );
    expect(toolVersion("mkvmerge v88.0 ('Æon') 64-bit")).toBe("88.0");
  });

  it("passes the server's own answers through", () => {
    expect(toolVersion("not installed")).toBe("not installed");
    expect(toolVersion(undefined)).toBe("—");
  });
});

// @vitest-environment node
import { existsSync } from "node:fs";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { brotliDecompressSync, gunzipSync } from "node:zlib";

import { afterEach, beforeEach, describe, expect, it } from "vitest";

import { compressAssets } from "./compress-assets.mjs";

describe("compressAssets", () => {
  let assets;

  beforeEach(async () => {
    assets = await mkdtemp(path.join(tmpdir(), "weir-assets-"));
  });

  afterEach(async () => {
    await rm(assets, { recursive: true, force: true });
  });

  it("writes Brotli and gzip copies that decompress to the built file", async () => {
    const script = "export const words = '" + "weir ".repeat(2000) + "';\n";
    await writeFile(path.join(assets, "index-abc123.js"), script);

    await compressAssets(assets);

    const brotli = await readFile(path.join(assets, "index-abc123.js.br"));
    const gzip = await readFile(path.join(assets, "index-abc123.js.gz"));
    expect(brotliDecompressSync(brotli).toString()).toBe(script);
    expect(gunzipSync(gzip).toString()).toBe(script);
  });

  it("compresses assets in nested folders", async () => {
    await mkdir(path.join(assets, "icons"));
    await writeFile(
      path.join(assets, "icons", "logo.svg"),
      "<svg>" + "<g/>".repeat(500) + "</svg>",
    );

    await compressAssets(assets);

    expect(existsSync(path.join(assets, "icons", "logo.svg.br"))).toBe(true);
  });

  it("leaves already-compressed formats alone", async () => {
    await writeFile(path.join(assets, "outfit.woff2"), Buffer.alloc(4096, 7));

    await compressAssets(assets);

    expect(existsSync(path.join(assets, "outfit.woff2.br"))).toBe(false);
    expect(existsSync(path.join(assets, "outfit.woff2.gz"))).toBe(false);
  });

  it("writes no copy that would be larger than the file", async () => {
    await writeFile(path.join(assets, "tiny.css"), "a{}");

    await compressAssets(assets);

    expect(existsSync(path.join(assets, "tiny.css.gz"))).toBe(false);
  });
});

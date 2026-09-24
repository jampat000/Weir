// Writes a Brotli (.br) and a gzip (.gz) copy beside each built asset in dist/assets, so the server sends
// compressed files without compressing on every request (CompressedStaticAssetsMiddleware, #712).
import { readdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { promisify } from "node:util";
import { brotliCompress, constants, gzip } from "node:zlib";

/** The asset types the server offers compressed: CompressedStaticAssetsMiddleware.CompressibleSuffixes. */
export const COMPRESSIBLE_EXTENSIONS = new Set([
  ".css",
  ".html",
  ".js",
  ".json",
  ".map",
  ".svg",
  ".txt",
  ".wasm",
  ".xml",
]);

const brotli = promisify(brotliCompress);
const gzipAsync = promisify(gzip);

/** Build time is spent once, so both use their smallest setting. */
const VARIANTS = [
  [
    ".br",
    (source) =>
      brotli(source, {
        params: {
          [constants.BROTLI_PARAM_QUALITY]: constants.BROTLI_MAX_QUALITY,
          [constants.BROTLI_PARAM_SIZE_HINT]: source.length,
        },
      }),
  ],
  [
    ".gz",
    (source) => gzipAsync(source, { level: constants.Z_BEST_COMPRESSION }),
  ],
];

/**
 * Compresses every compressible file under `assetsDir`. A copy that would be no smaller than the file is not
 * written, so the server sends the file itself.
 * @returns {Promise<{ files: number, originalBytes: number, brotliBytes: number }>}
 */
export async function compressAssets(assetsDir) {
  const totals = { files: 0, originalBytes: 0, brotliBytes: 0 };
  const entries = await readdir(assetsDir, {
    withFileTypes: true,
    recursive: true,
  });
  for (const entry of entries) {
    const file = path.join(entry.parentPath, entry.name);
    if (
      !entry.isFile() ||
      !COMPRESSIBLE_EXTENSIONS.has(path.extname(file).toLowerCase())
    ) {
      continue;
    }

    const source = await readFile(file);
    totals.files += 1;
    totals.originalBytes += source.length;
    for (const [suffix, compress] of VARIANTS) {
      const compressed = await compress(source);
      const kept = compressed.length < source.length;
      if (kept) await writeFile(file + suffix, compressed);
      if (suffix === ".br")
        totals.brotliBytes += kept ? compressed.length : source.length;
    }
  }
  return totals;
}

const invokedDirectly =
  process.argv[1] !== undefined &&
  import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;

if (invokedDirectly) {
  const webRoot = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)),
    "..",
  );
  const totals = await compressAssets(path.join(webRoot, "dist", "assets"));
  const kib = (bytes) => `${Math.round(bytes / 1024)} KiB`;
  console.log(
    `[compress-assets] ${totals.files} files: ${kib(totals.originalBytes)} as built, ${kib(totals.brotliBytes)} with Brotli.`,
  );
}

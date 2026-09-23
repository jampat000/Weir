/**
 * One way to say a size, everywhere. Two screens that read the same number must print it the same way, or a
 * person reasonably wonders which one is right: Live's "saved 318 MB" and Library's "318 MB back" come from
 * here, as does every size in a file's panel.
 *
 * Binary units, because that is what a disk reports. Enough decimals to be useful and no more: whole numbers
 * once a figure is big enough for a decimal to be noise.
 */
export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null || !Number.isFinite(bytes)) return "";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = Math.abs(bytes);
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  const shown =
    value >= 100 || unit === 0
      ? Math.round(value)
      : value.toFixed(unit >= 3 ? 2 : 1);
  return `${bytes < 0 ? "-" : ""}${shown} ${units[unit]}`;
}

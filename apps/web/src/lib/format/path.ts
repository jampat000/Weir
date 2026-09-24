/** The last part of a Windows or POSIX path: the file's own name. */
export function baseName(path: string): string {
  return path.split(/[\\/]/).filter(Boolean).at(-1) ?? path;
}

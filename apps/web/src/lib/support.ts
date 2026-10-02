export function normalizeSupportUrl(
  raw: string | null | undefined,
): string | null {
  const value = (raw ?? "").trim();
  if (value === "") {
    return null;
  }
  try {
    const parsed = new URL(value);
    if (parsed.protocol !== "https:" && parsed.protocol !== "http:") {
      return null;
    }
    return parsed.toString();
  } catch {
    return null;
  }
}

/** Where to support Weir's development, when this build was given a link; otherwise nothing about it shows. */
export const SUPPORT_URL = normalizeSupportUrl(
  import.meta.env.VITE_SUPPORT_URL,
);

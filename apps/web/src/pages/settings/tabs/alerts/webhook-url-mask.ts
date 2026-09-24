const SAVED_SUFFIX = " (saved)";
const MASKED_TOKEN = "••••";
const MASKED_SEGMENT = "…";
const UNREADABLE_URL = `${MASKED_TOKEN}${SAVED_SUFFIX}`;

/**
 * Shows a saved webhook's host and shape without the token in its path, so a channel row never
 * displays a value that could be replayed to post as it. Anything that will not parse as an
 * absolute URL is treated as if it were all token.
 */
export function maskWebhookUrl(url: string): string {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return UNREADABLE_URL;
  }

  const segments = parsed.pathname.split("/").filter(Boolean);
  if (segments.length < 2) {
    const tail = segments.length > 0 ? `/${MASKED_SEGMENT}` : "";
    return `${parsed.hostname}${tail}${SAVED_SUFFIX}`;
  }

  const leading = segments.slice(0, -2);
  const path = [...leading, MASKED_SEGMENT, MASKED_TOKEN].join("/");
  return `${parsed.hostname}/${path}${SAVED_SUFFIX}`;
}

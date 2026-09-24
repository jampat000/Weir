import { parseActivityDetail } from "../../lib/activity/detail";

/** "relative_media_path" -> "Relative media path". */
function humanizeKey(key: string): string {
  const words = key.replaceAll("_", " ");
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** A JSON value in plain words: yes/no for booleans, a joined list for arrays, "—" for nothing. */
function humanizeValue(value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "boolean") return value ? "Yes" : "No";
  if (Array.isArray(value)) {
    return value.length === 0
      ? "None"
      : value.map((item) => humanizeValue(item)).join(", ");
  }
  if (typeof value === "object") {
    return Object.entries(value as Record<string, unknown>)
      .map(([key, entry]) => `${humanizeKey(key)}: ${humanizeValue(entry)}`)
      .join("; ");
  }
  return String(value);
}

/**
 * An event's detail as plain fields instead of a JSON string. Every event type that does not have a
 * dedicated detail view (`FileProgressDetail`, `RemuxPassDetail`, ...) falls back to this, so nobody
 * reading Activity ever sees a raw `{...}` blob.
 */
export function StructuredDetailFacts({ detail }: { detail: string }) {
  const parsed = parseActivityDetail(detail);
  if (!parsed) {
    return <p className="mm-activity-item__more-text">{detail}</p>;
  }
  const entries = Object.entries(parsed).filter(
    ([, value]) => value !== null && value !== undefined && value !== "",
  );
  if (entries.length === 0) {
    return null;
  }
  return (
    <dl className="mm-kv">
      {entries.map(([key, value]) => (
        <div key={key}>
          <dt>{humanizeKey(key)}</dt>
          <dd>{humanizeValue(value)}</dd>
        </div>
      ))}
    </dl>
  );
}

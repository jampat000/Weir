import { SUPPORT_URL } from "../../../../lib/support";

/** Supporting Weir: optional, so it is one small link, and nothing at all in a build that has no link to give. */
export function SupportSection() {
  if (!SUPPORT_URL) return null;
  return (
    <a
      href={SUPPORT_URL}
      target="_blank"
      rel="noreferrer"
      className="mm-sys-link"
      title="Weir is free to use. Support is optional."
      data-testid="suite-settings-support"
    >
      Support Weir →
    </a>
  );
}

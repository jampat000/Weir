import type { ActivityTone } from "../../../lib/activity/activity-display";

const PATHS: Record<ActivityTone, string> = {
  success: "m5 12 5 5 9-10",
  info: "M12 8v.01M12 12v4",
  warning: "M12 8v5M12 17v.01",
  error: "m7 7 10 10M17 7 7 17",
};

/** The glyph that says how a line turned out, drawn in the tone's colour by the stylesheet. */
export function StreamIcon({ tone }: { tone: ActivityTone }) {
  return (
    <svg
      viewBox="0 0 24 24"
      width="16"
      height="16"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={PATHS[tone]} />
    </svg>
  );
}

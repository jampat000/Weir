import type { StatusMeaning } from "../../../lib/ui/status-meaning";

const INFORMATION = "M12 8v.01M12 12v4";

const PATHS: Record<StatusMeaning, string> = {
  done: "m5 12 5 5 9-10",
  todo: INFORMATION,
  doing: INFORMATION,
  idle: INFORMATION,
  attention: "M12 8v5M12 17v.01",
  broken: "m7 7 10 10M17 7 7 17",
};

/** The glyph that says how a line turned out, drawn in the colour of its meaning by the stylesheet. */
export function StreamIcon({ meaning }: { meaning: StatusMeaning }) {
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
      <path d={PATHS[meaning]} />
    </svg>
  );
}

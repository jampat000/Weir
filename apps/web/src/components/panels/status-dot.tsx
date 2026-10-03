import { classNames } from "../../lib/ui/class-names";
import type { StatusMeaning } from "../../lib/ui/status-meaning";

/** The dot of a status. It is decoration: the words beside it always say the state too. */
export function StatusDot({
  meaning,
  className,
}: {
  meaning: StatusMeaning;
  className?: string;
}) {
  return (
    <span
      className={classNames("mm-status-dot", className)}
      data-status={meaning}
      aria-hidden="true"
    />
  );
}

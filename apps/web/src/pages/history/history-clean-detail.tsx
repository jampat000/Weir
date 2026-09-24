import { baseName } from "../../lib/format/path";
import type { LibraryClean } from "../../lib/processing/library-cleans-api";
import { useAppDateFormatter } from "../../lib/ui/mm-format-date";
import { useNarrowDetailFocus } from "./use-narrow-detail-focus";

/** How a library clean's outcome reads as a heading. */
const OUTCOME_LEAD: Record<LibraryClean["outcome"], string> = {
  cleaned: "Cleaned in place.",
  skipped: "Already matched the library's rules.",
  failed: "The clean failed.",
};

/**
 * A library clean in full (#695): what it did to a file already in the library, read from the Activity
 * event the clean wrote. There is no further record to open and nothing to queue again from here — a
 * clean is re-run from the library it belongs to, not from History.
 */
export function HistoryCleanDetail({ clean }: { clean: LibraryClean }) {
  const { sectionRef, titleRef } = useNarrowDetailFocus(clean.id);
  const formatWhen = useAppDateFormatter();

  return (
    <section
      ref={sectionRef}
      className="mm-history-detail"
      aria-labelledby="history-detail-title"
      data-testid="history-detail"
    >
      <p className="mm-history-detail__eyebrow">{clean.library_name}</p>
      <h2
        id="history-detail-title"
        ref={titleRef}
        tabIndex={-1}
        className="mm-history-detail__title"
      >
        {baseName(clean.relative_path)}
      </h2>
      <p className="mm-history-detail__lead">
        {OUTCOME_LEAD[clean.outcome]} {formatWhen(clean.recorded_at)}
      </p>
      <p className="mm-history-note">{clean.detail}</p>
    </section>
  );
}

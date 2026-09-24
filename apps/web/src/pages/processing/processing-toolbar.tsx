import { Link } from "react-router-dom";

import { formatBytes } from "../../lib/format/bytes";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import {
  useProcessingFilesAtOnceQuery,
  useProcessingOverviewStatsQuery,
} from "../../lib/processing/queries";
import { plural } from "../../lib/ui/mm-plural";
import { HandedBackFigure } from "./handed-back-figure";
import type { WorkSource } from "./processing-model";

export type Filter = "all" | WorkSource;

const FILTERS = [
  ["all", "Everything"],
  ["download", "New downloads"],
  ["library", "Library cleaning"],
] as const;

/** Today's figures are counted over the last day. */
export const TODAY_DAYS = 1;
const PENDING = "…";

/**
 * "2 files at once · a download is left alone for 60 s after it stops changing": the two settings
 * that decide how fast files move through here. The wait is only named when every library agrees.
 */
function settingsNote(
  filesAtOnce: number | null,
  libraries: ProcessingLibrary[],
): string {
  const ages = new Set(
    libraries
      .filter((library) => library.enabled)
      .map((library) => library.min_file_age_seconds),
  );
  const [age] = ages;
  return [
    filesAtOnce != null
      ? `${plural(filesAtOnce, "file", "files")} at once`
      : "",
    ages.size === 1 && age > 0
      ? `new downloads wait ${age} seconds after they stop changing`
      : "",
  ]
    .filter(Boolean)
    .join(" · ");
}

/** The bar where every other page has its tabs: what to show, and today's figures. */
export function ProcessingToolbar({
  filter,
  onFilter,
  now,
}: {
  filter: Filter;
  onFilter: (filter: Filter) => void;
  now: number;
}) {
  const today = useProcessingOverviewStatsQuery(TODAY_DAYS);
  const filesAtOnce = useProcessingFilesAtOnceQuery();
  const libraries = useProcessingLibrariesQuery();
  const stats = today.data;
  const note = settingsNote(
    filesAtOnce.data?.effective_files_at_once ?? null,
    libraries.data ?? [],
  );
  return (
    <>
      <div className="mm-live-toolbar">
        <div className="mm-live-seg" role="group" aria-label="Show work from">
          {FILTERS.map(([id, label]) => (
            <button
              key={id}
              type="button"
              aria-pressed={filter === id}
              onClick={() => onFilter(id)}
            >
              {label}
            </button>
          ))}
        </div>
        <div className="mm-live-toolbar__figures">
          <div className="mm-live-figure">
            <span className="mm-live-figure__label">Handed back today</span>
            <span
              className="mm-live-figure__value"
              data-testid="live-done-today"
            >
              {stats ? stats.files_processed.toLocaleString() : PENDING}
            </span>
          </div>
          <div className="mm-live-figure">
            <span className="mm-live-figure__label">Saved today</span>
            <span className="mm-live-figure__value">
              {stats
                ? formatBytes(stats.net_space_saved_bytes) || "0 B"
                : PENDING}
            </span>
          </div>
          <HandedBackFigure filter={filter} now={now} />
        </div>
      </div>
      <p className="mm-live-toolbar__note">
        {note ? `${note} · ` : ""}
        <Link to="/settings?tab=performance">Change in Settings</Link>
      </p>
    </>
  );
}

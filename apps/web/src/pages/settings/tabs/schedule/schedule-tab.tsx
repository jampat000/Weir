import { useId, useState } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import { QuietSection } from "../../../../components/shared/quiet-section";
import {
  isHttpErrorFromApi,
  isLikelyNetworkFailure,
} from "../../../../lib/api/error-guards";
import { canEdit } from "../../../../lib/auth/can-edit";
import { useMeQuery } from "../../../../lib/auth/queries";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import { useProcessingLibrariesQuery } from "../../../../lib/processing/libraries-queries";
import { useAppSettingsQuery } from "../../../../lib/settings/queries";
import { useNow } from "../../../../lib/ui/use-now";
import { LibraryHoursEditor, LibraryHoursRow } from "./library-hours";
import { savedZone, TimeZoneRow } from "./time-zone-row";
import { TimersSection } from "./timers-section";

/** Often enough that "open until" and "it is 14:32 there" stay true while the page is open. */
const CLOCK_TICK_MS = 30_000;
const FALLBACK_ZONE = "UTC";

function LoadFailed({ error }: { error: unknown }) {
  return (
    <ul className="mm-interrupt" role="alert">
      <li className="mm-interrupt__item">
        <span className="mm-interrupt__text">
          <strong className="font-semibold">Could not load schedules.</strong>{" "}
          {isLikelyNetworkFailure(error)
            ? "Check that the Weir API is running."
            : isHttpErrorFromApi(error)
              ? "Sign in, then try again."
              : "Request failed."}
        </span>
      </li>
    </ul>
  );
}

function LibrariesSection({
  headingId,
  libraries,
  zone,
  now,
  editable,
}: {
  headingId: string;
  libraries: ProcessingLibrary[];
  zone: string;
  now: Date;
  editable: boolean;
}) {
  const [editing, setEditing] = useState<number | null>(null);
  const ordered = [...libraries].sort(
    (x, y) => x.display_order - y.display_order,
  );
  const editingLibrary = ordered.find((l) => l.id === editing) ?? null;
  return (
    <QuietSection headingId={headingId} heading="Libraries">
      <p className="mm-quiet-note">
        When each library may start work. A file already being processed when
        its hours end is finished, not stopped.
      </p>
      {ordered.length === 0 ? (
        <p className="mt-4 text-sm text-mm-text3">
          Add a library under Libraries to schedule it here.
        </p>
      ) : (
        <div className="mm-quiet-table-wrap mt-4">
          <table className="mm-quiet-table mm-schedule-table">
            <thead>
              <tr>
                <th scope="col">Library</th>
                <th scope="col">Week (Mon to Sun, midnight to midnight)</th>
                <th scope="col">Right now</th>
                <th scope="col">Looks for new files</th>
                <th scope="col">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {ordered.map((library) => (
                <LibraryHoursRow
                  key={library.id}
                  library={library}
                  zone={zone}
                  now={now}
                  editing={editing === library.id}
                  editable={editable}
                  onToggleEdit={() =>
                    setEditing(editing === library.id ? null : library.id)
                  }
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
      {editingLibrary ? (
        <LibraryHoursEditor
          key={editingLibrary.id}
          library={editingLibrary}
          editable={editable}
          onDone={() => setEditing(null)}
        />
      ) : null}
    </QuietSection>
  );
}

/**
 * Settings › Schedule: the time zone, a week per library with its hours editor below, and when
 * Weir's own jobs run.
 */
export function ScheduleTab() {
  const me = useMeQuery();
  const settings = useAppSettingsQuery();
  const libraries = useProcessingLibrariesQuery();
  const now = new Date(useNow(CLOCK_TICK_MS));
  const ids = useId();
  const editable = canEdit(me.data?.role);

  if (settings.isPending || libraries.isPending || me.isPending) {
    return <PageLoading label="Loading schedules" />;
  }
  if (settings.isError || libraries.isError) {
    return <LoadFailed error={settings.error ?? libraries.error} />;
  }

  return (
    <div className="mm-quiet-stack" data-testid="processing-schedules-section">
      <TimeZoneRow
        key={savedZone(settings.data)}
        settings={settings.data}
        editable={editable}
        now={now}
      />
      <LibrariesSection
        headingId={`${ids}-libraries`}
        libraries={libraries.data}
        zone={settings.data.app_timezone || FALLBACK_ZONE}
        now={now}
        editable={editable}
      />
      <TimersSection headingId={`${ids}-timers`} settings={settings.data} />
    </div>
  );
}

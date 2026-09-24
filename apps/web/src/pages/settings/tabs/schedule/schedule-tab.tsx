import { useId, useState } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import { QuietSection } from "../../../../components/shared/quiet-section";
import { canEdit } from "../../../../lib/auth/can-edit";
import { useMeQuery } from "../../../../lib/auth/queries";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import { useProcessingLibrariesQuery } from "../../../../lib/processing/libraries-queries";
import { useAppSettingsQuery } from "../../../../lib/settings/queries";
import { useNow } from "../../../../lib/ui/use-now";
import { SaveModelNote } from "../../save-model-note";
import { SettingsLoadError } from "../../settings-load-error";
import { useLeaveConfirmation, useUnsavedChanges } from "../../unsaved-changes";
import { LibraryHoursEditor, LibraryHoursRow } from "./library-hours";
import { effectiveGrid } from "./schedule-model";
import { savedZone, TimeZoneRow } from "./time-zone-row";
import { TimersSection } from "./timers-section";

/** Often enough that "open until" and "it is 14:32 there" stay true while the page is open. */
const CLOCK_TICK_MS = 30_000;
const FALLBACK_ZONE = "UTC";

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
  const [grid, setGrid] = useState("");
  const ordered = [...libraries].sort(
    (x, y) => x.display_order - y.display_order,
  );
  const editingLibrary = ordered.find((l) => l.id === editing) ?? null;
  const dirty =
    editingLibrary !== null && grid !== effectiveGrid(editingLibrary);
  const thing = editingLibrary ? `${editingLibrary.name}'s hours` : null;
  useUnsavedChanges(dirty ? thing : null);
  const { confirmLeave, dialog } = useLeaveConfirmation();

  const openEditor = (library: ProcessingLibrary) => {
    setEditing(library.id);
    setGrid(effectiveGrid(library));
  };
  const closeEditor = () => setEditing(null);
  const requestClose = () => confirmLeave(dirty ? thing : null, closeEditor);
  const toggleEdit = (library: ProcessingLibrary) => {
    if (editing === library.id) requestClose();
    else confirmLeave(dirty ? thing : null, () => openEditor(library));
  };

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
                  onToggleEdit={() => toggleEdit(library)}
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
          grid={grid}
          onGrid={setGrid}
          editable={editable}
          onSaved={closeEditor}
          onClose={requestClose}
        />
      ) : null}
      {dialog}
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
    return <SettingsLoadError what="schedules" />;
  }

  return (
    <div className="mm-quiet-stack" data-testid="processing-schedules-section">
      <SaveModelNote model="explicit" />
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

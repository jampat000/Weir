import { Link } from "react-router-dom";

import {
  QuietFieldGroup,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { SidePanel } from "../../../../components/shared/side-panel";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import type {
  ProcessingLibrary,
  ProcessingRuleSet,
} from "../../../../lib/processing/libraries-api";
import { useProcessingRejectSupportQuery } from "../../../../lib/processing/libraries-queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { effectiveGrid, windowNow } from "../schedule/schedule-model";
import { LibraryCleaningSettings } from "./library-cleaning-settings";
import { LibraryFoldersGroup } from "./library-folders-group";
import { LibraryHardwareFold } from "./library-hardware-fold";
import {
  LibraryIntakeGroup,
  LibraryReadinessGroup,
} from "./library-intake-groups";
import { LibraryManagerSetup } from "./library-manager-setup";
import {
  LibraryCapacityGroup,
  LibraryOutputGroup,
} from "./library-output-groups";
import type { LibraryFormBinding } from "./library-settings";

/** When the library may run, read from the schedule that owns its hours. */
function runHoursText(library: ProcessingLibrary | undefined): string {
  if (!library) return "Any time, until you choose hours. ";
  return windowNow(effectiveGrid(library), undefined, new Date()).kind === "any"
    ? "Any time. "
    : "On the hours chosen for it. ";
}

/**
 * The library editor, as a slide-over: every setting of one library in groups, the rarely changed
 * ones folded away. A new library has no id yet, so its existing-files settings wait for the first save.
 */
export function LibraryEditor({
  open,
  library,
  binding,
  ruleSets,
  connections,
  onSave,
  onClose,
}: {
  open: boolean;
  /** The saved library being edited; undefined while adding one. */
  library: ProcessingLibrary | undefined;
  binding: LibraryFormBinding;
  ruleSets: ProcessingRuleSet[];
  connections: MediaManagerConnection[];
  onSave: () => void;
  onClose: () => void;
}) {
  const { form, update, editable } = binding;
  // Reject is offered for the manager chosen in the editor, saved or not.
  const rejectSupport = useProcessingRejectSupportQuery(
    form.manager_connection_id ? [Number(form.manager_connection_id)] : [],
    open,
  );

  return (
    <SidePanel
      open={open}
      title={library ? "Edit library" : "Add library"}
      eyebrow="Settings · Libraries"
      subtitle={
        library
          ? "Changes take effect on the next scan; nothing already running is disturbed."
          : "A library is a watched folder, a work area and an output folder."
      }
      onClose={onClose}
      dataTestId="processing-library-form"
    >
      <div className="mm-quiet-stack">
        <LibraryFoldersGroup
          binding={binding}
          ruleSets={ruleSets}
          connections={connections}
        />
        <LibraryManagerSetup
          mediaType={form.media_type}
          watchedFolder={form.watched_folder}
          outputFolder={form.output_folder}
          removeOriginal={form.remove_original_after_success}
          editable={editable}
          onUseFolders={(watched, output) =>
            update({
              watched_folder: watched ?? form.watched_folder,
              output_folder: output ?? form.output_folder,
            })
          }
        />
        <LibraryIntakeGroup binding={binding} />
        <LibraryReadinessGroup binding={binding} />
        <LibraryOutputGroup binding={binding} />
        <LibraryCapacityGroup binding={binding} rejectSupport={rejectSupport} />
        <LibraryHardwareFold binding={binding} />
        <QuietFieldGroup title="Files already in your library">
          {library ? (
            <LibraryCleaningSettings
              libraryId={library.id}
              editable={editable}
            />
          ) : (
            <p className="mm-quiet-note">
              Save the library first, then add the folders its existing files
              sit in.
            </p>
          )}
        </QuietFieldGroup>
        {/* The hours are drawn in Settings › Schedule beside every other library's week, so the two never disagree. */}
        <QuietFieldGroup title="When this library may run">
          <p className="mm-quiet-note" data-testid="processing-library-hours">
            {runHoursText(library)}
            <Link className="mm-schedule-link" to="/settings?tab=schedule">
              Change the hours in Schedule
            </Link>
          </p>
        </QuietFieldGroup>
        <div className={quietActionRowClass}>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "primary" })}
            onClick={onSave}
            disabled={!editable || !form.name.trim()}
            data-testid="processing-library-save"
          >
            Save
          </button>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "tertiary" })}
            onClick={onClose}
          >
            Cancel
          </button>
        </div>
      </div>
    </SidePanel>
  );
}

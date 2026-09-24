import { useMutation } from "@tanstack/react-query";
import { useState } from "react";
import { Link } from "react-router-dom";

import {
  QuietFieldGroup,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { SidePanel } from "../../../../components/shared/side-panel";
import { errorMessage } from "../../../../lib/api/error-message";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import type { ProcessingRuleSet } from "../../../../lib/processing/rule-sets-api";
import { useProcessingRejectSupportQuery } from "../../../../lib/processing/libraries-queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { SaveModelNote } from "../../save-model-note";
import { useLeaveConfirmation, useUnsavedChanges } from "../../unsaved-changes";
import { effectiveGrid, windowNow } from "../schedule/schedule-model";
import { useLibraryCleaningDraft } from "./library-cleaning-draft";
import { LibraryCleaningSettings } from "./library-cleaning-settings";
import { LibraryFoldersGroup } from "./library-folders-group";
import { sameLibraryForm, type LibraryForm } from "./library-form";
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
 * ones folded away. Nothing in it is saved until Save, including the existing-files settings, and
 * closing it with edits asks first. A new library has no id yet, so its existing-files settings wait
 * for the first save.
 */
export function LibraryEditor({
  library,
  initial,
  editable,
  ruleSets,
  connections,
  onSave,
  onClose,
}: {
  /** The saved library being edited; undefined while adding one. */
  library: ProcessingLibrary | undefined;
  initial: LibraryForm;
  editable: boolean;
  ruleSets: ProcessingRuleSet[];
  connections: MediaManagerConnection[];
  /** Saves the library's own fields; the editor saves the rest after it. */
  onSave: (form: LibraryForm) => Promise<unknown>;
  onClose: () => void;
}) {
  const [form, setForm] = useState(initial);
  const binding: LibraryFormBinding = {
    form,
    update: (patch) => setForm((current) => ({ ...current, ...patch })),
    editable,
  };
  const cleaning = useLibraryCleaningDraft(library?.id);
  const saveAll = useMutation({
    mutationFn: async () => {
      await onSave(form);
      await cleaning.save();
    },
    onSuccess: onClose,
  });
  const dirty = !sameLibraryForm(form, initial) || cleaning.dirty;
  const thing = library ? library.name : "the new library";
  useUnsavedChanges(dirty ? thing : null);
  const { confirmLeave, dialog } = useLeaveConfirmation();
  const close = () => confirmLeave(dirty ? thing : null, onClose);
  // Reject is offered for the manager chosen in the editor, saved or not.
  const rejectSupport = useProcessingRejectSupportQuery(
    form.manager_connection_id ? [Number(form.manager_connection_id)] : [],
    true,
  );

  return (
    <>
      <SidePanel
        open
        title={library ? "Edit library" : "Add library"}
        eyebrow="Settings · Libraries"
        subtitle={
          library
            ? "Changes take effect on the next scan; nothing already running is disturbed."
            : "A library is a watched folder, a work area and an output folder."
        }
        onClose={close}
        dataTestId="processing-library-form"
      >
        <div className="mm-quiet-stack">
          <SaveModelNote model="explicit" />
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
              binding.update({
                watched_folder: watched ?? form.watched_folder,
                output_folder: output ?? form.output_folder,
              })
            }
          />
          <LibraryIntakeGroup binding={binding} />
          <LibraryReadinessGroup binding={binding} />
          <LibraryOutputGroup binding={binding} />
          <LibraryCapacityGroup
            binding={binding}
            rejectSupport={rejectSupport}
          />
          <LibraryHardwareFold binding={binding} />
          <QuietFieldGroup title="Files already in your library">
            {library ? (
              <LibraryCleaningSettings
                libraryId={library.id}
                cleaning={cleaning}
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
          {saveAll.isError ? (
            <p
              className="mm-status-text--failed text-sm"
              role="alert"
              data-testid="processing-library-save-error"
            >
              {errorMessage(saveAll.error, "That library could not be saved.")}
            </p>
          ) : null}
          <div className={quietActionRowClass}>
            <button
              type="button"
              className={mmActionButtonClass({ variant: "primary" })}
              onClick={() => saveAll.mutate()}
              disabled={!editable || !form.name.trim() || saveAll.isPending}
              data-testid="processing-library-save"
            >
              {saveAll.isPending ? "Saving…" : "Save"}
            </button>
            <button
              type="button"
              className={mmActionButtonClass({ variant: "tertiary" })}
              disabled={saveAll.isPending}
              onClick={close}
            >
              Cancel
            </button>
          </div>
        </div>
      </SidePanel>
      {dialog}
    </>
  );
}

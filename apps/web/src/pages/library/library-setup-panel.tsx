import { useMutation } from "@tanstack/react-query";

import { quietActionRowClass } from "../../components/shared/quiet-section";
import { SidePanel } from "../../components/shared/side-panel";
import { errorMessage } from "../../lib/api/error-message";
import { useCanEdit } from "../../lib/auth/can-edit";
import { useProcessingRuleSetsQuery } from "../../lib/processing/libraries-queries";
import type { LibrarySettings } from "../../lib/processing/library-mode-api";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { SaveModelNote } from "../settings/save-model-note";
import { useLeaveConfirmation } from "../settings/unsaved-changes";
import { useLibrarySetupDraft } from "./library-setup-draft";
import { LibrarySetupForm } from "./library-setup-form";

/**
 * The setup of one library's existing files, as a slide-over from the Library page: folders, rules profile,
 * daily clean and safety checks together. Nothing is saved until Save, and closing with edits asks first.
 */
export function LibrarySetupPanel({
  workflow,
  settings,
  onClose,
}: {
  workflow: ProcessingLibrary;
  settings: LibrarySettings;
  onClose: () => void;
}) {
  const editable = useCanEdit();
  const ruleSets = useProcessingRuleSetsQuery();
  const setup = useLibrarySetupDraft(workflow.id, settings);
  const save = useMutation({ mutationFn: setup.save, onSuccess: onClose });
  const { confirmLeave, dialog } = useLeaveConfirmation();
  const close = () =>
    confirmLeave(setup.dirty ? `the setup of ${workflow.name}` : null, onClose);

  return (
    <>
      <SidePanel
        open
        title="Library setup"
        eyebrow="Library"
        subtitle={workflow.name}
        onClose={close}
        dataTestId="library-setup-panel"
      >
        <div className="mm-quiet-stack">
          <SaveModelNote model="explicit" />
          <LibrarySetupForm
            workflow={workflow}
            ruleSets={ruleSets.data ?? []}
            setup={setup}
            editable={editable}
          />
          {save.isError ? (
            <p
              className="mm-status-text--failed text-sm"
              role="alert"
              data-testid="library-setup-save-error"
            >
              {errorMessage(save.error, "That setup could not be saved.")}
            </p>
          ) : null}
          <div className={quietActionRowClass}>
            <button
              type="button"
              className={mmActionButtonClass({ variant: "primary" })}
              onClick={() => save.mutate()}
              disabled={!editable || !setup.dirty || save.isPending}
              data-testid="library-setup-save"
            >
              {save.isPending ? "Saving…" : "Save"}
            </button>
            <button
              type="button"
              className={mmActionButtonClass({ variant: "tertiary" })}
              disabled={save.isPending}
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

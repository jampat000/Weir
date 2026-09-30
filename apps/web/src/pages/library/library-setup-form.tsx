import { useState } from "react";
import { Link } from "react-router-dom";

import { QuietFieldGroup } from "../../components/shared/quiet-section";
import { Field } from "../../components/shared/field";
import { MmOnOffSwitch } from "../../components/ui/mm-on-off-switch";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import type { ProcessingRuleSet } from "../../lib/processing/rule-sets-api";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import type { LibrarySetupDraft } from "./library-setup-draft";
import { AddFolder, FolderList } from "./library-setup-folders";
import { KeepOriginalSettings } from "./library-setup-originals";

/** Switching the daily clean on always asks first, because it removes tracks without asking each time. */
function DailyCleanWarning({
  onAccept,
  onCancel,
}: {
  onAccept: () => void;
  onCancel: () => void;
}) {
  return (
    <div
      className="mm-library-setup__confirm"
      role="alertdialog"
      aria-label="Switch on the daily clean"
    >
      <p>
        Once a day Weir will check these folders and remove the tracks this
        library&apos;s rules profile removes, from every file that has them,
        without asking each time. Removed tracks cannot be put back. Files you
        have left alone are never touched.
      </p>
      <div className="mm-library-setup__confirm-actions">
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          onClick={onAccept}
        >
          Switch it on
        </button>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "tertiary" })}
          onClick={onCancel}
        >
          Cancel
        </button>
      </div>
    </div>
  );
}

/** "" means the library follows its workflow's profile; the option says which one that is. */
function RulesProfileField({
  workflow,
  ruleSets,
  chosenId,
  editable,
  onChange,
}: {
  workflow: ProcessingLibrary;
  ruleSets: ProcessingRuleSet[];
  chosenId: number | null;
  editable: boolean;
  onChange: (ruleSetId: number | null) => void;
}) {
  const workflowProfile = ruleSets.find(
    (set) => set.id === workflow.rule_set_id,
  );
  return (
    <Field
      label="Rules profile"
      hint="Decides which audio and subtitle tracks a clean keeps in this library. Create and edit profiles under Settings › Rules."
    >
      <select
        className="mm-input"
        value={chosenId === null ? "" : String(chosenId)}
        disabled={!editable}
        data-testid="library-rules-profile"
        onChange={(event) =>
          onChange(
            event.target.value === "" ? null : Number(event.target.value),
          )
        }
      >
        <option value="">
          {workflowProfile
            ? `Same as the workflow (${workflowProfile.name})`
            : "Same as the workflow"}
        </option>
        {ruleSets.map((ruleSet) => (
          <option key={ruleSet.id} value={String(ruleSet.id)}>
            {ruleSet.name}
          </option>
        ))}
      </select>
    </Field>
  );
}

/**
 * Everything that sets up the files already in one library: where they sit, which rules clean them, the daily
 * clean and the safety checks a clean makes. Nothing here is saved until the panel's Save.
 */
export function LibrarySetupForm({
  workflow,
  ruleSets,
  setup,
  editable,
}: {
  workflow: ProcessingLibrary;
  ruleSets: ProcessingRuleSet[];
  setup: LibrarySetupDraft;
  editable: boolean;
}) {
  const [warning, setWarning] = useState(false);
  const { draft, change } = setup;
  const folders = draft.library_folders;
  const libraryId = workflow.id;

  return (
    <div className="mm-library-setup" data-testid="library-setup-form">
      <QuietFieldGroup
        title="Library folders"
        detail="Where the files you already have sit. Weir only changes one when you ask, or once a day if you switch that on below."
      >
        <FolderList
          folders={folders}
          editable={editable}
          onRemove={(folder) =>
            change({ library_folders: folders.filter((f) => f !== folder) })
          }
        />
        {editable ? (
          <AddFolder
            onAdd={(folder) =>
              change({
                library_folders: Array.from(new Set([...folders, folder])),
              })
            }
          />
        ) : null}
      </QuietFieldGroup>

      <QuietFieldGroup title="Rules">
        <RulesProfileField
          workflow={workflow}
          ruleSets={ruleSets}
          chosenId={draft.library_rule_set_id}
          editable={editable}
          onChange={(ruleSetId) => change({ library_rule_set_id: ruleSetId })}
        />
      </QuietFieldGroup>

      <QuietFieldGroup title="Daily clean">
        <MmOnOffSwitch
          id={`library-${libraryId}-daily-clean`}
          label="Check and clean once a day, inside this workflow's hours"
          enabled={draft.library_schedule_enabled}
          disabled={!editable || folders.length === 0}
          onChange={(next) =>
            next
              ? setWarning(true)
              : change({ library_schedule_enabled: false })
          }
        />
        {warning ? (
          <DailyCleanWarning
            onAccept={() => {
              setWarning(false);
              change({ library_schedule_enabled: true });
            }}
            onCancel={() => setWarning(false)}
          />
        ) : null}
        <p className="mm-library-setup__hint">
          <Link className="mm-schedule-link" to="/settings?tab=schedule">
            Change the hours in Schedule
          </Link>
        </p>
      </QuietFieldGroup>

      <QuietFieldGroup title="Before a file is cleaned">
        <MmOnOffSwitch
          id={`library-${libraryId}-skip-redownload`}
          label="Skip a file when cleaning it would make the media manager download it again"
          enabled={draft.skip_if_manager_would_redownload}
          disabled={!editable}
          onChange={(next) =>
            change({ skip_if_manager_would_redownload: next })
          }
        />
        <MmOnOffSwitch
          id={`library-${libraryId}-clean-seeding`}
          label="Clean a file that is still seeding"
          enabled={draft.clean_hardlinked_files}
          disabled={!editable}
          onChange={(next) => change({ clean_hardlinked_files: next })}
        />
        <p className="mm-library-setup__hint">
          Off by default: a file that is still seeding shares its data with the
          download, so cleaning it frees no space.
        </p>
      </QuietFieldGroup>

      <QuietFieldGroup title="After a file is cleaned">
        <KeepOriginalSettings
          libraryId={libraryId}
          enabled={draft.keep_original_after_clean}
          originalsFolder={draft.originals_folder}
          editable={editable}
          onChangeEnabled={(next) =>
            change({ keep_original_after_clean: next })
          }
          onChangeFolder={(folder) => change({ originals_folder: folder })}
        />
      </QuietFieldGroup>
    </div>
  );
}

import type { ReactNode } from "react";
import { Field } from "../../../../components/shared/field";
import { QuietDisclosure } from "../../../../components/shared/quiet-section";
import { useProcessingOperatorSettingsQuery } from "../../../../lib/processing/queries";
import { COLLISION_OPTIONS } from "./library-options";
import {
  SelectSetting,
  TextSetting,
  ToggleSetting,
  type LibraryFormBinding,
} from "./library-settings";

/** A blank "most files at once" is no limit of its own, which the server keeps as 0. */
const NO_OWN_LIMIT = "0";

/** Sidecars, timestamps, and what happens when the destination already exists. */
export function LibraryOutputGroup({
  binding,
}: {
  binding: LibraryFormBinding;
}) {
  return (
    <QuietDisclosure
      title="Output safety"
      detail="Control the files that travel with the video, timestamps, the space kept free, and what happens when the destination already exists."
    >
      <div className="mm-editor-grid">
        <TextSetting
          binding={binding}
          name="sidecar_patterns_csv"
          label="Files that travel with the video"
          width="medium"
          placeholder=".srt,.nfo,.jpg"
        />
        <SelectSetting
          binding={binding}
          name="output_collision_policy"
          label="Existing output"
          options={COLLISION_OPTIONS}
        />
        <TextSetting
          binding={binding}
          name="minimum_free_disk_space_gb"
          label="Keep at least this many GB free on the drive this workflow writes to"
          width="medium"
          placeholder="5"
          hint="A file that would leave less is put on hold, and Weir tries again when there is room. 0 turns this off."
        />
      </div>
      <div className="mm-library-toggles">
        <ToggleSetting
          binding={binding}
          name="preserve_original_timestamps"
          label="Preserve original timestamps"
        />
        <ToggleSetting
          binding={binding}
          name="remove_original_after_success"
          label="New downloads: after cleaning, delete the original download"
          hint="Turn off if your download client is still seeding it — Sonarr, Radarr or your client will clean it up."
        />
      </div>
    </QuietDisclosure>
  );
}

/**
 * This workflow's own limit as a share of what Weir runs at once in total: "up to 3 of Weir's 4", blank for no limit of its
 * own. A number above the total is refused when saved, and a saved one above a lowered total says so here.
 */
function MostFilesAtOnceSetting({ binding }: { binding: LibraryFormBinding }) {
  const { form, update, editable } = binding;
  const total = useProcessingOperatorSettingsQuery().data?.max_concurrent_files;
  const own = Number.parseInt(form.max_concurrent_files, 10);
  const hasOwnLimit = Number.isFinite(own) && own > 0;
  let hint: ReactNode = "Blank means no limit of its own.";
  if (total !== undefined && !hasOwnLimit) {
    hint = `No limit of its own: it can use any of Weir's ${total} files at once, alongside the other workflows.`;
  } else if (total !== undefined && own <= total) {
    hint = `Up to ${own} of Weir's ${total} files at once.`;
  } else if (total !== undefined) {
    hint = (
      <span role="alert" data-testid="workflow-limited-by-total">
        Weir runs {total} files at once in total, so this workflow is limited to{" "}
        {total}. Choose {total} or fewer, or raise Files at once in Setup ›
        Performance › Speed.
      </span>
    );
  }
  return (
    <Field
      label="Most files at once from this workflow"
      width="short"
      hint={hint}
    >
      <input
        className="mm-input"
        inputMode="numeric"
        value={
          form.max_concurrent_files === NO_OWN_LIMIT
            ? ""
            : form.max_concurrent_files
        }
        placeholder="No limit"
        onChange={(event) =>
          update({
            max_concurrent_files: event.target.value.trim() || NO_OWN_LIMIT,
          })
        }
        disabled={!editable}
      />
    </Field>
  );
}

/** How many files at once and in what order. */
export function LibraryCapacityGroup({
  binding,
}: {
  binding: LibraryFormBinding;
}) {
  return (
    <QuietDisclosure
      title="Capacity"
      detail="Priority is relative: higher-numbered workflows are offered work first."
    >
      <div className="mm-editor-grid">
        <MostFilesAtOnceSetting binding={binding} />
        <TextSetting
          binding={binding}
          name="priority"
          label="Priority (higher goes first)"
          width="short"
          placeholder="0"
        />
      </div>
    </QuietDisclosure>
  );
}

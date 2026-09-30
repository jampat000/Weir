import { QuietFieldGroup } from "../../../../components/shared/quiet-section";
import { COLLISION_OPTIONS, FILES_AT_ONCE_OPTIONS } from "./library-options";
import {
  SelectSetting,
  TextSetting,
  ToggleSetting,
  type LibraryFormBinding,
} from "./library-settings";

/** Sidecars, timestamps, and what happens when the destination already exists. */
export function LibraryOutputGroup({
  binding,
}: {
  binding: LibraryFormBinding;
}) {
  return (
    <QuietFieldGroup
      title="Output safety"
      detail="Control the files that travel with the video, timestamps, and what happens when the destination already exists."
    >
      <div className="mm-field-row">
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
          label="After cleaning, remove the original download"
          hint="Turn off if your download client is still seeding it — Sonarr, Radarr or your client will clean it up."
        />
      </div>
    </QuietFieldGroup>
  );
}

/** How many files at once and in what order. */
export function LibraryCapacityGroup({
  binding,
}: {
  binding: LibraryFormBinding;
}) {
  return (
    <QuietFieldGroup
      title="Capacity"
      detail="Priority is relative: higher-numbered workflows are offered work first."
    >
      <div className="mm-field-row">
        <SelectSetting
          binding={binding}
          name="max_concurrent_files"
          label="Files at once"
          options={FILES_AT_ONCE_OPTIONS}
          hint="Only to hold this workflow below Files at once in Performance, so it cannot take every slot."
        />
        <TextSetting
          binding={binding}
          name="priority"
          label="Priority (higher goes first)"
          width="short"
          placeholder="0"
        />
      </div>
    </QuietFieldGroup>
  );
}

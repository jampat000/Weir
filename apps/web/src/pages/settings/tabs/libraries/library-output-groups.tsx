import type { ReactNode } from "react";
import { Field } from "../../../../components/shared/field";
import { QuietFieldGroup } from "../../../../components/shared/quiet-section";
import type { ProcessingFailurePolicy } from "../../../../lib/processing/libraries-api";
import type { useProcessingRejectSupportQuery } from "../../../../lib/processing/libraries-queries";
import { useProcessingOperatorSettingsQuery } from "../../../../lib/processing/queries";
import { COLLISION_OPTIONS, FAILURE_POLICY_HINTS } from "./library-options";
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
    <QuietFieldGroup
      title="Output safety"
      detail="Control the files that travel with the video, timestamps, the space kept free, and what happens when the destination already exists."
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
    </QuietFieldGroup>
  );
}

/**
 * What happens when retries run out. Reject is offered only when the linked manager can take one;
 * a library already on Reject keeps the option so its saved value still shows.
 */
function FailurePolicySetting({
  binding,
  rejectSupport,
}: {
  binding: LibraryFormBinding;
  rejectSupport: ReturnType<typeof useProcessingRejectSupportQuery>;
}) {
  const { form, update, editable } = binding;
  const rejectAvailable = rejectSupport.data?.available === true;
  return (
    <Field label="When retries run out" width="medium">
      <select
        className="mm-input"
        value={form.failure_policy}
        onChange={(event) =>
          update({
            failure_policy: event.target.value as ProcessingFailurePolicy,
          })
        }
        disabled={!editable}
      >
        <option value="pass_through">Hand the original back unchanged</option>
        <option value="hold">Keep it until someone acts</option>
        <option
          value="reject"
          disabled={!rejectAvailable && form.failure_policy !== "reject"}
        >
          Reject the release so a different one is found
        </option>
      </select>
      <span className="mm-field__hint">
        {FAILURE_POLICY_HINTS[form.failure_policy]}
      </span>
      <span className="mm-field__hint" data-testid="reject-support">
        {rejectSupport.isLoading
          ? "Checking whether Reject is available…"
          : rejectSupport.data
            ? `Reject: ${rejectSupport.data.reason}`
            : null}
      </span>
    </Field>
  );
}

/**
 * This workflow's own limit as a share of what Weir runs at once in total: "up to 3 of Weir's 4", blank for no limit of its
 * own. A number above the total is refused when saved, and one that Performance has since been lowered under shows here.
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
        {total}. Choose {total} or fewer, or raise Files at once in Settings ›
        Performance.
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

/** How many files at once, in what order, and how failures are retried. */
export function LibraryCapacityGroup({
  binding,
  rejectSupport,
}: {
  binding: LibraryFormBinding;
  rejectSupport: ReturnType<typeof useProcessingRejectSupportQuery>;
}) {
  return (
    <QuietFieldGroup
      title="Capacity and recovery"
      detail="Priority is relative: higher-numbered workflows are offered work first."
    >
      <div className="mm-field-row">
        <MostFilesAtOnceSetting binding={binding} />
        <TextSetting
          binding={binding}
          name="priority"
          label="Priority (higher goes first)"
          width="short"
          placeholder="0"
        />
        <TextSetting
          binding={binding}
          name="max_attempts"
          label="Maximum automatic attempts"
          width="short"
          placeholder="3"
        />
        <TextSetting
          binding={binding}
          name="retry_backoff_seconds"
          label="First retry delay (seconds)"
          width="short"
          placeholder="300"
        />
      </div>
      <div className="mm-library-toggles">
        <ToggleSetting
          binding={binding}
          name="retry_execution_failures"
          label="Retry processing failures"
        />
        <ToggleSetting
          binding={binding}
          name="retry_preflight_failures"
          label="Retry files that failed the pre-check"
          hint="Usually leave this off: retrying does not repair an unsupported or malformed file."
        />
      </div>
      <FailurePolicySetting binding={binding} rejectSupport={rejectSupport} />
    </QuietFieldGroup>
  );
}

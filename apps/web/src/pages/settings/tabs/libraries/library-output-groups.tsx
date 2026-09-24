import { Field } from "../../../../components/shared/field";
import { QuietFieldGroup } from "../../../../components/shared/quiet-section";
import type { ProcessingFailurePolicy } from "../../../../lib/processing/libraries-api";
import type { useProcessingRejectSupportQuery } from "../../../../lib/processing/libraries-queries";
import {
  COLLISION_OPTIONS,
  FAILURE_POLICY_HINTS,
  FILES_AT_ONCE_OPTIONS,
} from "./library-options";
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
      detail="Priority is relative: higher-numbered libraries are offered work first."
    >
      <div className="mm-field-row">
        <SelectSetting
          binding={binding}
          name="max_concurrent_files"
          label="Files at once"
          options={FILES_AT_ONCE_OPTIONS}
          hint="Only to hold this library below Files at once in Performance, so it cannot take every slot."
        />
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

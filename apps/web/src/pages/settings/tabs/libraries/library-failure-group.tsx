import { Field } from "../../../../components/shared/field";
import { QuietFieldGroup } from "../../../../components/shared/quiet-section";
import type { ProcessingFailurePolicy } from "../../../../lib/processing/libraries-api";
import type { useProcessingRejectSupportQuery } from "../../../../lib/processing/libraries-queries";
import { FAILURE_POLICY_HINTS } from "./library-options";
import {
  TextSetting,
  ToggleSetting,
  type LibraryFormBinding,
} from "./library-settings";

/**
 * What happens once retries run out. Reject is offered only when the linked manager can take one;
 * a library already on Reject keeps the option so its saved value still shows.
 */
function RetriesRunOutSetting({
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

/** How often and how many times a failed file is tried again, and what happens to it once Weir gives up. */
export function LibraryFailureGroup({
  binding,
  rejectSupport,
}: {
  binding: LibraryFormBinding;
  rejectSupport: ReturnType<typeof useProcessingRejectSupportQuery>;
}) {
  return (
    <QuietFieldGroup
      title="When a file fails"
      detail="Weir tries a failed file again on its own, up to the limit below. A file it gives up on shows as Failed in History, with the reason."
    >
      <div className="mm-field-row">
        <TextSetting
          binding={binding}
          name="max_attempts"
          label="Maximum automatic attempts"
          width="medium"
          placeholder="3"
          hint="The first try counts: 3 means the first try and two retries."
        />
        <TextSetting
          binding={binding}
          name="retry_backoff_seconds"
          label="First retry delay (seconds)"
          width="medium"
          placeholder="300"
          hint="Weir looks again shortly after this wait. Each later wait is twice as long, up to an hour."
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
      <RetriesRunOutSetting binding={binding} rejectSupport={rejectSupport} />
    </QuietFieldGroup>
  );
}

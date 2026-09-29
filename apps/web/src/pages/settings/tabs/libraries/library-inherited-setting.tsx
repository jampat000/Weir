import { Field } from "../../../../components/shared/field";
import type { ProcessingOperatorSettingsOut } from "../../../../lib/processing/types";
import { useProcessingOperatorSettingsQuery } from "../../../../lib/processing/queries";
import {
  USES_PERFORMANCE_SETTING,
  type LibraryTextField,
} from "./library-form";
import type { LibraryFormBinding } from "./library-settings";

/**
 * A number a library can leave to Settings › Performance. Left alone it says what that setting is now; "Set for this
 * library" starts a value of its own from it, and "Use the Performance setting" hands it back.
 */
export function InheritedNumberSetting({
  binding,
  name,
  label,
  unit,
  performanceField,
  hint,
}: {
  binding: LibraryFormBinding;
  name: LibraryTextField;
  label: string;
  /** Read after the number: "seconds", "MB". */
  unit: string;
  /** The Performance setting this one follows. */
  performanceField: keyof Pick<
    ProcessingOperatorSettingsOut,
    "min_file_age_seconds" | "min_input_file_size_mb"
  >;
  hint?: string;
}) {
  const performance = useProcessingOperatorSettingsQuery();
  const inherited = performance.data?.[performanceField] ?? null;
  const current =
    inherited === null ? null : `${inherited.toLocaleString()} ${unit}`;

  if (binding.form[name] === USES_PERFORMANCE_SETTING) {
    return (
      <div className="mm-field mm-field--medium">
        <span className="mm-field__label">{label}</span>
        <span>
          {current === null
            ? "Uses the Performance setting"
            : `Uses the Performance setting: ${current}`}
        </span>
        <button
          type="button"
          className="mm-link-button"
          disabled={!binding.editable}
          onClick={() => binding.update({ [name]: String(inherited ?? 0) })}
        >
          Set for this workflow
        </button>
        {hint ? <span className="mm-field__hint">{hint}</span> : null}
      </div>
    );
  }

  return (
    <Field
      label={label}
      width="short"
      hint={
        <>
          {hint ? <span>{hint} </span> : null}
          <button
            type="button"
            className="mm-link-button"
            disabled={!binding.editable}
            onClick={() => binding.update({ [name]: USES_PERFORMANCE_SETTING })}
          >
            {current === null
              ? "Use the Performance setting instead"
              : `Use the Performance setting instead (${current})`}
          </button>
        </>
      }
    >
      <input
        className="mm-input"
        inputMode="numeric"
        value={binding.form[name]}
        onChange={(e) => binding.update({ [name]: e.target.value })}
        disabled={!binding.editable}
      />
    </Field>
  );
}

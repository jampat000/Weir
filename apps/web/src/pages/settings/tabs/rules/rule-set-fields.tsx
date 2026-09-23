import type { ReactNode } from "react";

import { Field } from "../../../../components/shared/field";
import {
  QuietDisclosure,
  QuietFieldGroup,
} from "../../../../components/shared/quiet-section";
import type { ProcessingRuleSetWrite } from "../../../../lib/processing/libraries-api";
import {
  DEFAULT_TRACK_NAME_TEMPLATE,
  SAMPLE_TRACK,
  previewTrackName,
  trackNameTemplateError,
  type TrackNamePreviewFlags,
} from "../../../../lib/processing/track-name-preview";
import { mmCheckboxControlClass } from "../../../../lib/ui/mm-control-roles";

type RuleSet = ProcessingRuleSetWrite;

/** The profile being edited, how to change one of its rules, and whether that is allowed right now. */
export type RuleSetBinding = {
  draft: RuleSet;
  change: <Key extends keyof RuleSet>(key: Key, value: RuleSet[Key]) => void;
  disabled: boolean;
};

type KeysOf<Value> = {
  [Key in keyof RuleSet]: RuleSet[Key] extends Value ? Key : never;
}[keyof RuleSet];

export function RuleToggle({
  binding,
  name,
  label,
  detail,
}: {
  binding: RuleSetBinding;
  name: KeysOf<boolean>;
  label: string;
  detail: string;
}) {
  return (
    <label className="mm-rule-toggle">
      <input
        type="checkbox"
        className={mmCheckboxControlClass}
        checked={binding.draft[name]}
        disabled={binding.disabled}
        onChange={(event) => binding.change(name, event.target.checked)}
      />
      <span>
        <span className="mm-rule-toggle__label">{label}</span>
        <span className="mm-rule-toggle__detail">{detail}</span>
      </span>
    </label>
  );
}

export function RuleSelect({
  binding,
  name,
  label,
  options,
  hint,
  disabled = binding.disabled,
  onChange = (value) => binding.change(name, value),
  width = "medium",
}: {
  binding: RuleSetBinding;
  name: KeysOf<string>;
  label: string;
  options: readonly { value: string; label: string }[];
  hint?: string;
  disabled?: boolean;
  onChange?: (value: string) => void;
  width?: "short" | "medium";
}) {
  return (
    <Field label={label} hint={hint} width={width}>
      <select
        className="mm-input"
        value={binding.draft[name]}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </Field>
  );
}

/**
 * A track name template with a live preview against a sample track (#498), or an inline error naming
 * the first unknown placeholder: the same rule the server enforces on save.
 */
export function TrackNameTemplateField({
  label,
  detail,
  value,
  disabled,
  sampleFlags,
  onChange,
}: {
  label: string;
  detail?: string;
  value: string;
  disabled: boolean;
  sampleFlags?: TrackNamePreviewFlags;
  onChange: (value: string) => void;
}) {
  const error = trackNameTemplateError(value);
  return (
    <label className="mm-field mm-field--wide">
      <span className="mm-field__label">{label}</span>
      <input
        className="mm-input"
        value={value}
        placeholder={DEFAULT_TRACK_NAME_TEMPLATE}
        disabled={disabled}
        aria-invalid={error !== null}
        onChange={(event) => onChange(event.target.value)}
      />
      {detail ? <span className="mm-field__hint">{detail}</span> : null}
      {error ? (
        <span role="alert" className="mm-field__hint">
          {error}
        </span>
      ) : (
        <span className="mm-field__hint">
          Preview:{" "}
          <span className="font-medium">
            {previewTrackName(value, { ...SAMPLE_TRACK, flags: sampleFlags })}
          </span>
        </span>
      )}
    </label>
  );
}

/** One numbered group of a profile's rules. */
export function RuleGroup({
  step,
  title,
  detail,
  children,
}: {
  step: number;
  title: string;
  detail: string;
  children: ReactNode;
}) {
  return (
    <QuietFieldGroup step={step} title={title} detail={detail}>
      <div className="space-y-3">{children}</div>
    </QuietFieldGroup>
  );
}

/** Rules that are off by default: closed, and saying how many inside are on so the fold never hides a change. */
export function RuleFold({
  title,
  detail,
  on,
  of,
  children,
}: {
  title: string;
  detail: string;
  on: number;
  of: number;
  children: ReactNode;
}) {
  return (
    <QuietDisclosure
      title={title}
      detail={detail}
      summaryWhenClosed={on === 0 ? "Off" : `${on} of ${of} on`}
      defaultOpen={on > 0}
    >
      <div className="space-y-3">{children}</div>
    </QuietDisclosure>
  );
}

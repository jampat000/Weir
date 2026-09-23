import { useId } from "react";

import { Field } from "../../../../components/shared/field";
import { MmMultiListboxPicker } from "../../../../components/ui/mm-multi-listbox-picker";
import { processingLanguageVariantsFor } from "../../../../lib/processing/language-variant-options";
import { PROCESSING_STREAM_LANGUAGE_OPTIONS } from "../../../../lib/processing/stream-language-options";
import { csvValues } from "./rule-set-model";

const LANGUAGE_OPTIONS = PROCESSING_STREAM_LANGUAGE_OPTIONS.map((option) => ({
  value: option.code,
  label: `${option.label} (${option.code})`,
}));

/**
 * Every base language followed by its regional variants as indented entries (#496), so a picker built
 * from a flat list still reads as "variants under their base language" without optgroup support.
 */
const LANGUAGE_OPTIONS_WITH_VARIANTS = LANGUAGE_OPTIONS.flatMap((option) => [
  option,
  ...processingLanguageVariantsFor(option.value).map((variant) => ({
    value: variant.code,
    label: `— ${variant.label} (${variant.code})`,
  })),
]);

const KNOWN_CODES = new Set(
  LANGUAGE_OPTIONS_WITH_VARIANTS.map((option) => option.value),
);

/** The known languages, plus any saved code Weir has no name for, so it can still be seen and removed. */
function languageOptionsFor(values: readonly string[]) {
  const custom = values
    .filter((value) => !KNOWN_CODES.has(value))
    .map((value) => ({ value, label: value }));
  return [...LANGUAGE_OPTIONS_WITH_VARIANTS, ...custom];
}

/** One language, with regional variants grouped under their base language (#496). */
export function LanguageSelectField({
  label,
  value,
  noneLabel,
  disabled,
  onChange,
}: {
  label: string;
  value: string;
  /** Offered first when the field may be left empty. */
  noneLabel?: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  const isCustom = value.length > 0 && !KNOWN_CODES.has(value);
  return (
    <Field label={label} width="medium">
      <select
        className="mm-input"
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      >
        {noneLabel ? <option value="">{noneLabel}</option> : null}
        {isCustom ? <option value={value}>{value}</option> : null}
        {LANGUAGE_OPTIONS.map((base) => {
          const variants = processingLanguageVariantsFor(base.value);
          if (variants.length === 0) {
            return (
              <option key={base.value} value={base.value}>
                {base.label}
              </option>
            );
          }
          return (
            <optgroup key={base.value} label={base.label}>
              <option value={base.value}>{base.label} (any variant)</option>
              {variants.map((variant) => (
                <option key={variant.code} value={variant.code}>
                  {variant.label} ({variant.code})
                </option>
              ))}
            </optgroup>
          );
        })}
      </select>
    </Field>
  );
}

/** Several languages, stored as a comma list. */
export function LanguageMultiField({
  label,
  value,
  disabled,
  onChange,
}: {
  label: string;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  const labelId = useId();
  const values = csvValues(value);
  return (
    <div className="mm-field mm-field--medium">
      <span id={labelId} className="mm-field__label">
        {label}
      </span>
      <MmMultiListboxPicker
        options={languageOptionsFor(values)}
        values={values}
        disabled={disabled}
        ariaLabelledBy={labelId}
        placeholder="Choose languages…"
        onChange={(next) => onChange(next.join(","))}
      />
    </div>
  );
}

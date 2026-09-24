import type { ReactNode } from "react";

import { Field, type FieldWidth } from "../../../../components/shared/field";
import type {
  LibraryForm,
  LibraryTextField,
  LibraryToggleField,
} from "./library-form";

/** The editor's values, how to change them, and whether this user may. */
export type LibraryFormBinding = {
  form: LibraryForm;
  update: (patch: Partial<LibraryForm>) => void;
  editable: boolean;
};

export type SettingOption = {
  value: string;
  label: string;
  disabled?: boolean;
};

export function TextSetting({
  binding,
  name,
  label,
  width,
  placeholder = "",
  hint,
}: {
  binding: LibraryFormBinding;
  name: LibraryTextField;
  label: string;
  width: FieldWidth;
  placeholder?: string;
  hint?: string;
}) {
  return (
    <Field label={label} hint={hint} width={width}>
      <input
        className="mm-input"
        value={binding.form[name]}
        placeholder={placeholder}
        onChange={(e) => binding.update({ [name]: e.target.value })}
        disabled={!binding.editable}
      />
    </Field>
  );
}

export function DateTimeSetting({
  binding,
  name,
  label,
}: {
  binding: LibraryFormBinding;
  name: LibraryTextField;
  label: string;
}) {
  return (
    <Field label={label} width="medium">
      <input
        type="datetime-local"
        className="mm-input"
        value={binding.form[name]}
        onChange={(e) => binding.update({ [name]: e.target.value })}
        disabled={!binding.editable}
      />
    </Field>
  );
}

export function SelectSetting({
  binding,
  name,
  label,
  options,
  hint,
  testId,
}: {
  binding: LibraryFormBinding;
  name: LibraryTextField;
  label: string;
  options: readonly SettingOption[];
  hint?: ReactNode;
  testId?: string;
}) {
  return (
    <Field label={label} width="medium" hint={hint}>
      <select
        className="mm-input"
        value={binding.form[name]}
        onChange={(e) => binding.update({ [name]: e.target.value })}
        disabled={!binding.editable}
        data-testid={testId}
      >
        {options.map((option) => (
          <option
            key={option.value}
            value={option.value}
            disabled={option.disabled}
          >
            {option.label}
          </option>
        ))}
      </select>
    </Field>
  );
}

export function ToggleSetting({
  binding,
  name,
  label,
  hint,
}: {
  binding: LibraryFormBinding;
  name: LibraryToggleField;
  label: string;
  hint?: string;
}) {
  return (
    <label className="mm-library-toggle">
      <input
        className="mt-0.5"
        type="checkbox"
        checked={binding.form[name]}
        onChange={(e) => binding.update({ [name]: e.target.checked })}
        disabled={!binding.editable}
      />
      <span>
        <span className="mm-library-toggle__label">{label}</span>
        {hint ? <span className="mm-library-toggle__hint">{hint}</span> : null}
      </span>
    </label>
  );
}

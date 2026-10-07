import { useId, type ReactNode } from "react";

import { Field, type FieldWidth } from "../../../../components/shared/field";
import { ServerFolderPickerButton } from "../../../../components/ui/server-folder-picker-button";
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

/**
 * A folder on the machine running Weir: typed, or chosen with Browse. Not wrapped in one label like
 * the other fields, because the folder picker opens inside it and a click in the picker would
 * otherwise land on the text box.
 */
export function FolderSetting({
  binding,
  name,
  label,
  placeholder = "",
  hint,
  lockedNote,
}: {
  binding: LibraryFormBinding;
  name: LibraryTextField;
  label: string;
  placeholder?: string;
  hint?: string;
  /** Says whose the folder is when a media manager owns it: the folder is then shown but cannot be changed here. */
  lockedNote?: string;
}) {
  const id = useId();
  const hintId = useId();
  const locked = lockedNote !== undefined;
  const note = lockedNote ?? hint;
  return (
    <div className="mm-field mm-field--wide">
      <label className="mm-field__label" htmlFor={id}>
        {label}
      </label>
      <div className="mm-folder-field">
        <input
          id={id}
          className="mm-input"
          value={binding.form[name]}
          placeholder={placeholder}
          aria-describedby={note ? hintId : undefined}
          onChange={(e) => binding.update({ [name]: e.target.value })}
          disabled={!binding.editable}
          readOnly={locked}
        />
        <ServerFolderPickerButton
          title={`Choose the ${label.toLowerCase()}`}
          value={binding.form[name]}
          disabled={!binding.editable || locked}
          onSelect={(path) => binding.update({ [name]: path })}
        />
      </div>
      {note ? (
        <span id={hintId} className="mm-field__hint">
          {note}
        </span>
      ) : null}
    </div>
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
  lockedOff,
}: {
  binding: LibraryFormBinding;
  name: LibraryToggleField;
  label: string;
  hint?: string;
  /** The reason this setting is off and cannot be changed; the saved value is left as it is. */
  lockedOff?: string;
}) {
  return (
    <label className="mm-library-toggle">
      <input
        className="mt-0.5"
        type="checkbox"
        checked={lockedOff === undefined && binding.form[name]}
        onChange={(e) => binding.update({ [name]: e.target.checked })}
        disabled={!binding.editable || lockedOff !== undefined}
      />
      <span>
        <span className="mm-library-toggle__label">{label}</span>
        {lockedOff !== undefined || hint ? (
          <span
            className="mm-library-toggle__hint"
            data-testid={lockedOff === undefined ? undefined : `${name}-locked`}
          >
            {lockedOff ?? hint}
          </span>
        ) : null}
      </span>
    </label>
  );
}

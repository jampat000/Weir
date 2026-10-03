import { useId, useState } from "react";

import { EyeIcon, EyeOffIcon } from "../../../../components/ui/eye-icons";

const EYE_SIZE = 18;

type FieldProps = {
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled: boolean;
  placeholder: string;
  autoComplete: string;
};

/** A labelled text field in the sign-in forms. */
export function SecurityField({ onChange, label, ...input }: FieldProps) {
  const id = useId();
  return (
    <div className="mm-field">
      <label className="mm-field__label" htmlFor={id}>
        {label}
      </label>
      <input
        {...input}
        id={id}
        type="text"
        className="mm-input"
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  );
}

/**
 * A password field with an eye inside it that shows or hides the text, named for what it reveals, e.g. "Show new
 * password". The toggle is a sibling of the label, not nested inside it: a label that wraps another interactive
 * control is invalid HTML and some browsers double-fire the click.
 *
 * Emptying the field hides the text again; the form remounts it (a new key) after each submit, so a shown password
 * never outlives the attempt it was typed for.
 */
export function RevealablePasswordField({
  onChange,
  label,
  revealLabel,
  disabled,
  autoComplete,
  ...input
}: FieldProps & { revealLabel: string }) {
  const id = useId();
  const [shown, setShown] = useState(false);
  const toggleLabel = shown ? `Hide ${revealLabel}` : `Show ${revealLabel}`;
  return (
    <div className="mm-field">
      <label className="mm-field__label" htmlFor={id}>
        {label}
      </label>
      <div className="mm-sys-secret">
        <input
          {...input}
          id={id}
          type={shown ? "text" : "password"}
          autoComplete={autoComplete}
          className="mm-input"
          disabled={disabled}
          onChange={(e) => {
            onChange(e.target.value);
            if (e.target.value.trim() === "") setShown(false);
          }}
        />
        <button
          type="button"
          className="mm-sys-secret__toggle"
          disabled={disabled}
          aria-label={toggleLabel}
          title={toggleLabel}
          onClick={() => setShown((prev) => !prev)}
        >
          {shown ? <EyeOffIcon size={EYE_SIZE} /> : <EyeIcon size={EYE_SIZE} />}
        </button>
      </div>
    </div>
  );
}

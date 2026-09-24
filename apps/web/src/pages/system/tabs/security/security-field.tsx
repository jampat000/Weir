import { useId, useState } from "react";

import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

type FieldProps = {
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled: boolean;
  placeholder: string;
  autoComplete: string;
};

/** A labelled field in the sign-in forms. */
export function SecurityField({
  type = "text",
  onChange,
  label,
  ...input
}: FieldProps & { type?: "text" | "password" }) {
  const id = useId();
  return (
    <label className="block" htmlFor={id}>
      <span className="text-sm text-mm-text2">{label}</span>
      <div className="mt-1 flex flex-wrap gap-2">
        <input
          {...input}
          id={id}
          type={type}
          className="mm-input mm-security-field"
          onChange={(e) => onChange(e.target.value)}
        />
      </div>
    </label>
  );
}

/**
 * A password field with a Show/Hide button that names what it reveals, e.g. "Show new password".
 * The button is a sibling of the `<label>`, not nested inside it: a `<label>` that wraps another
 * interactive control is invalid HTML and some browsers double-fire the click.
 *
 * Emptying the field hides the text again; the form remounts it (a new `key`) after each submit, so
 * a shown password never outlives the attempt it was typed for.
 */
export function RevealablePasswordField({
  onChange,
  label,
  revealLabel,
  disabled,
  ...input
}: FieldProps & { revealLabel: string }) {
  const id = useId();
  const [shown, setShown] = useState(false);
  return (
    <div className="block">
      <label className="text-sm text-mm-text2" htmlFor={id}>
        {label}
      </label>
      <div className="mt-1 flex flex-wrap gap-2">
        <input
          {...input}
          id={id}
          type={shown ? "text" : "password"}
          className="mm-input mm-security-field"
          disabled={disabled}
          onChange={(e) => {
            onChange(e.target.value);
            if (e.target.value.trim() === "") setShown(false);
          }}
        />
        <button
          type="button"
          className={mmActionButtonClass({ variant: "tertiary" })}
          disabled={disabled}
          onClick={() => setShown((prev) => !prev)}
        >
          {shown ? `Hide ${revealLabel}` : `Show ${revealLabel}`}
        </button>
      </div>
    </div>
  );
}

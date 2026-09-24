import { useState, type ReactNode } from "react";

import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

type FieldProps = {
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled: boolean;
  placeholder: string;
  autoComplete: string;
};

function FieldFrame({
  label,
  children,
}: {
  label: string;
  children: ReactNode;
}) {
  return (
    <label className="block">
      <span className="text-sm text-mm-text2">{label}</span>
      <div className="mt-1 flex flex-wrap gap-2">{children}</div>
    </label>
  );
}

/** A labelled field in the sign-in forms. */
export function SecurityField({
  type = "text",
  onChange,
  label,
  ...input
}: FieldProps & { type?: "text" | "password" }) {
  return (
    <FieldFrame label={label}>
      <input
        {...input}
        type={type}
        className="mm-input mm-security-field"
        onChange={(e) => onChange(e.target.value)}
      />
    </FieldFrame>
  );
}

/**
 * A password field with Show/Hide. Emptying it hides the text again; the form remounts it (a new
 * `key`) after each submit, so a shown password never outlives the attempt it was typed for.
 */
export function RevealablePasswordField({
  onChange,
  label,
  disabled,
  ...input
}: FieldProps) {
  const [shown, setShown] = useState(false);
  return (
    <FieldFrame label={label}>
      <input
        {...input}
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
        {shown ? "Hide" : "Show"}
      </button>
    </FieldFrame>
  );
}

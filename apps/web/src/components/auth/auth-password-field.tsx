import { useState } from "react";

import { EyeIcon, EyeOffIcon } from "../ui/eye-icons";

/**
 * A labelled password field for the sign-in and first-run screens, with a Show/Hide toggle that sits
 * beside the field rather than inside its `<label>` (a control nested in a label is invalid HTML and
 * duplicates activation in some browsers).
 */
export function AuthPasswordField({
  id,
  testId,
  name,
  label,
  revealLabel,
  value,
  onChange,
  autoComplete,
  required,
  minLength,
  maxLength,
}: {
  id: string;
  testId: string;
  name: string;
  label: string;
  /** What the toggle says it shows or hides, e.g. "password" or "confirmation". */
  revealLabel: string;
  value: string;
  onChange: (value: string) => void;
  autoComplete: string;
  required?: boolean;
  minLength?: number;
  maxLength?: number;
}) {
  const [shown, setShown] = useState(false);
  return (
    <>
      <label className="mm-auth-label" htmlFor={id}>
        {label}
      </label>
      <div className="mm-auth-password-field">
        <input
          id={id}
          data-testid={testId}
          name={name}
          type={shown ? "text" : "password"}
          autoComplete={autoComplete}
          className="mm-auth-input mm-auth-input--password-toggle"
          value={value}
          onChange={(e) => {
            const next = e.target.value;
            onChange(next);
            // A shown password should not outlive the text it was revealing.
            if (next === "") setShown(false);
          }}
          required={required}
          minLength={minLength}
          maxLength={maxLength}
        />
        <button
          type="button"
          className="mm-auth-password-toggle"
          data-testid={`${testId}-toggle`}
          aria-label={shown ? `Hide ${revealLabel}` : `Show ${revealLabel}`}
          title={shown ? `Hide ${revealLabel}` : `Show ${revealLabel}`}
          onClick={() => setShown((v) => !v)}
        >
          {shown ? <EyeOffIcon /> : <EyeIcon />}
        </button>
      </div>
    </>
  );
}

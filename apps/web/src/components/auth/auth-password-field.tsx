import { useState } from "react";

function EyeIcon() {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width="20"
      height="20"
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden
    >
      <path
        d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Z"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx="12" cy="12" r="3" stroke="currentColor" strokeWidth="2" />
    </svg>
  );
}

function EyeOffIcon() {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width="20"
      height="20"
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden
    >
      <path
        d="M10.73 5.08A10.4 10.4 0 0 1 12 5c7 0 10 7 10 7a13.2 13.2 0 0 1-1.67 2.68M6.61 6.61A13.5 13.5 0 0 0 2 12s4 7 10 7c1.38 0 2.65-.21 3.78-.6M9.88 9.88a3 3 0 1 0 4.24 4.24"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="m2 2 20 20"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
      />
    </svg>
  );
}

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

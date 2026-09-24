import { cloneElement, isValidElement, useId, type ReactNode } from "react";

/**
 * How wide a setting's control is, chosen by what goes in it (weir-tokens.css): a number or time,
 * a choice or short name, or a path or sentence.
 */
export type FieldWidth = "short" | "medium" | "wide";

/**
 * One setting in the one shape every setting has: its label, its control, and the line under it
 * that explains the control. The control inside carries `mm-input` and takes the field's width, so
 * two fields that do the same job are the same size on every screen. The label wraps only the name
 * and the control, so clicking the words focuses it and it is named by them; the hint sits outside
 * the label and is linked instead with `aria-describedby`, so it explains the control without also
 * becoming part of its accessible name (a hint mentioning another field's name must not make that
 * name ambiguous).
 */
export function Field({
  label,
  hint,
  width = "medium",
  children,
}: {
  label: ReactNode;
  hint?: ReactNode;
  width?: FieldWidth;
  children: ReactNode;
}) {
  const hintId = useId();
  const control =
    hint && isValidElement<{ "aria-describedby"?: string }>(children)
      ? cloneElement(children, { "aria-describedby": hintId })
      : children;
  return (
    <div className={`mm-field mm-field--${width}`}>
      <label className="mm-field">
        <span className="mm-field__label">{label}</span>
        {control}
      </label>
      {hint ? (
        <span className="mm-field__hint" id={hintId}>
          {hint}
        </span>
      ) : null}
    </div>
  );
}

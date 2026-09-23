import type { ReactNode } from "react";

/**
 * How wide a setting's control is, chosen by what goes in it (weir-tokens.css): a number or time,
 * a choice or short name, or a path or sentence.
 */
export type FieldWidth = "short" | "medium" | "wide";

/**
 * One setting in the one shape every setting has (docs/design/content-language.md, `.mm-field`):
 * its label, its control, and the line under it that explains the control. The control inside
 * carries `mm-input` and takes the field's width, so two fields that do the same job are the same
 * size on every screen (James, 23 Sep 2026: "its not aligned, its not uniform, its not
 * standardised"). The label wraps the control, so clicking the words focuses it and it is named by
 * them.
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
  return (
    <label className={`mm-field mm-field--${width}`}>
      <span className="mm-field__label">{label}</span>
      {children}
      {hint ? <span className="mm-field__hint">{hint}</span> : null}
    </label>
  );
}

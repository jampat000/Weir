import { useEffect, useRef, useState } from "react";

const COPIED_MS = 2_000;

/**
 * A quiet "Copy" beside a value someone has to type somewhere else, such as an address. It says "Copied" for a
 * moment afterwards; where the browser will not let the page use the clipboard it stays "Copy" and does nothing.
 * With no value there is nothing to copy, so it is disabled.
 * `label` names what is copied for a screen reader: "Copy {label}".
 */
export function CopyLink({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(timer.current), []);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
    } catch {
      setCopied(false);
      return;
    }
    setCopied(true);
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setCopied(false), COPIED_MS);
  };

  return (
    <button
      type="button"
      className="mm-quiet-link"
      onClick={() => void copy()}
      disabled={!value}
      aria-label={`Copy ${label}`}
    >
      {copied ? "Copied" : "Copy"}
    </button>
  );
}

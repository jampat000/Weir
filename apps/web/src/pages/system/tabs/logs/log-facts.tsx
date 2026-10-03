import type { ReactNode } from "react";

/** The row of links and buttons at the foot of an open row. */
export const LOG_ACTIONS = "flex flex-wrap items-center gap-x-4 gap-y-2";

export type LogFact = {
  label: string;
  value: ReactNode;
  /** The value is an identifier or technical text: set in monospace. */
  mono?: boolean;
};

/** A fact, or what a condition left in its place when there is nothing to say. */
type MaybeFact = LogFact | false | null | undefined | "";

/** What an open row says about itself: label and value pairs, those with nothing to say left out. */
export function LogFacts({ facts }: { facts: readonly MaybeFact[] }) {
  const shown = facts.filter((fact): fact is LogFact => Boolean(fact));
  if (shown.length === 0) return null;
  return (
    <dl className="mm-log-facts">
      {shown.map((fact) => (
        <div key={fact.label}>
          <dt>{fact.label}</dt>
          <dd className={fact.mono ? "mm-log-facts__mono" : undefined}>
            {fact.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

/** Verbatim diagnostic text: the box is what marks it as raw output. */
export function LogRawText({ label, text }: { label: string; text: string }) {
  return (
    <div>
      <p className="mm-log-raw__label">{label}</p>
      <pre className="mm-log-raw__text">{text}</pre>
    </div>
  );
}

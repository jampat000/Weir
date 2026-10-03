import { useId, type ReactNode } from "react";

import { Panel } from "../panels/panel";

/**
 * A section of a page: a panel with its heading and links in the header and its content padded below.
 * A section inside a tab panel is one level down, so it takes `level={3}`.
 */
export function QuietSection({
  headingId,
  heading,
  level = 2,
  count,
  aside,
  children,
  id,
  "data-testid": dataTestId,
}: {
  headingId: string;
  heading: string;
  level?: 2 | 3;
  /** A quiet line after the heading. */
  count?: ReactNode;
  aside?: ReactNode;
  children: ReactNode;
  id?: string;
  "data-testid"?: string;
}) {
  return (
    <Panel
      title={heading}
      headingId={headingId}
      headingLevel={level}
      count={count}
      aside={aside}
      padded
      id={id}
      dataTestId={dataTestId}
    >
      {children}
    </Panel>
  );
}

/**
 * One group of fields inside a long configuration form. A form without boxes still needs its
 * grouping, so it survives as type and whitespace: uppercase eyebrow type over a hairline, like a
 * table's header row, labelling the block beneath it without becoming a second box.
 */
export function QuietFieldGroup({
  step,
  title,
  detail,
  aside,
  children,
  className,
}: {
  /** Shown before the title as a "step n of five" cue. Decorative: it is kept out of
   *  the heading's accessible name, which stays exactly the group's own title. */
  step?: number;
  title: string;
  detail?: ReactNode;
  aside?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  const headingId = useId();
  return (
    <section
      className={`mm-quiet-group${className ? ` ${className}` : ""}`}
      aria-labelledby={headingId}
    >
      <div className="mm-quiet-group__head">
        <span className="mm-quiet-group__name">
          {step === undefined ? null : (
            <span aria-hidden="true" className="mm-quiet-group__step">
              {step}
            </span>
          )}
          <h3 id={headingId} className="mm-quiet-group__title">
            {title}
          </h3>
        </span>
        {aside ?? null}
      </div>
      {detail ? <p className="mm-quiet-group__detail">{detail}</p> : null}
      <div className="mm-quiet-group__body">{children}</div>
    </section>
  );
}

/**
 * A group that starts closed, for the settings almost nobody changes. `<details>` rather than
 * state: it opens without JavaScript, answers space and enter, and find-in-page opens it to show a
 * match. `summaryWhenClosed` ("3 of 9 on") keeps a change visible while the fold is shut.
 */
export function QuietDisclosure({
  title,
  detail,
  summaryWhenClosed,
  defaultOpen = false,
  children,
  "data-testid": dataTestId,
}: {
  title: string;
  detail?: ReactNode;
  summaryWhenClosed?: string;
  defaultOpen?: boolean;
  children: ReactNode;
  "data-testid"?: string;
}) {
  const headingId = useId();
  return (
    <details
      className="mm-quiet-fold"
      open={defaultOpen}
      data-testid={dataTestId}
    >
      <summary className="mm-quiet-fold__head">
        {/* A real heading, closed or open: folding a group away must not take it out of the
            document's outline, or off the list a screen reader navigates by. */}
        <h3 id={headingId} className="mm-quiet-fold__title">
          {title}
        </h3>
        {summaryWhenClosed ? (
          <span className="mm-quiet-fold__state">{summaryWhenClosed}</span>
        ) : null}
      </summary>
      <div role="region" aria-labelledby={headingId}>
        {detail ? <p className="mm-quiet-fold__detail">{detail}</p> : null}
        <div className="mm-quiet-fold__body">{children}</div>
      </div>
    </details>
  );
}

/** The hairline above a form's own Save row. */
export const quietActionRowClass = "mm-quiet-actions";

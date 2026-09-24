import { useId, type ReactNode } from "react";

/**
 * The quiet body of a page: a heading on the left, its links on the right, a hairline under both,
 * then the content. No cards, no panels, no wells.
 * A section inside a tab panel is one level down, so it takes `level={3}`.
 */
export function QuietSection({
  headingId,
  heading,
  level = 2,
  aside,
  children,
  id,
  "data-testid": dataTestId,
}: {
  headingId: string;
  heading: string;
  level?: 2 | 3;
  aside?: ReactNode;
  children: ReactNode;
  id?: string;
  "data-testid"?: string;
}) {
  const Heading = level === 2 ? "h2" : "h3";
  return (
    <section
      className="mm-quiet-section"
      aria-labelledby={headingId}
      id={id}
      data-testid={dataTestId}
    >
      <div className="mm-quiet-section__head">
        <Heading id={headingId} className="mm-quiet-section__title">
          {heading}
        </Heading>
        {aside ? <div className="mm-quiet-section__aside">{aside}</div> : null}
      </div>
      <div className="mm-quiet-section__body">{children}</div>
    </section>
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
  return (
    <details
      className="mm-quiet-fold"
      open={defaultOpen}
      data-testid={dataTestId}
    >
      <summary className="mm-quiet-fold__head">
        {/* A real heading, closed or open: folding a group away must not take it out of the
            document's outline, or off the list a screen reader navigates by. */}
        <h3 className="mm-quiet-fold__title">{title}</h3>
        {summaryWhenClosed ? (
          <span className="mm-quiet-fold__state">{summaryWhenClosed}</span>
        ) : null}
      </summary>
      {detail ? <p className="mm-quiet-fold__detail">{detail}</p> : null}
      <div className="mm-quiet-fold__body">{children}</div>
    </details>
  );
}

/** The hairline above a form's own Save row. */
export const quietActionRowClass = "mm-quiet-actions";

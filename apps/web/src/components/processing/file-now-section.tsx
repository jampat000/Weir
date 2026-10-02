import type { ReactElement } from "react";

/** Where a file is on its way through Weir at this moment, and how it is getting on. */
export type FileNow = {
  /** The station it is at: "Processing". */
  stage: string;
  /** What is happening to it, in a few words: "76% · 2 s left", "Waiting to settle". */
  status: string;
  /** Work is being done on it this moment. */
  working: boolean;
  /** How far along, 0 to 100, or null while only "it is under way" is known. */
  progress: number | null;
  /** Everything Weir knows about it right now, a sentence a fact. */
  facts: readonly string[];
};

/** A file that is on the Pipeline: its live state, which its history does not have yet. */
export function FileNowSection({ now }: { now: FileNow }): ReactElement {
  return (
    <section className="mm-story-now" aria-label="Right now">
      <h3 className="mm-story-now__heading">Right now</h3>
      <p className="mm-story-now__stage">
        {now.working ? (
          <span aria-hidden="true" className="mm-story-now__pulse" />
        ) : null}
        {now.stage}
      </p>
      <p className="mm-story-now__status">{now.status}</p>
      {now.progress !== null ? (
        <span
          role="progressbar"
          aria-label="Progress"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(now.progress)}
          className="mm-story-now__bar"
        >
          <i style={{ width: `${now.progress}%` }} />
        </span>
      ) : null}
      {now.facts.length > 0 ? (
        <ul className="mm-story-now__facts">
          {now.facts.map((fact) => (
            <li key={fact}>{fact}</li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}

import type { CSSProperties } from "react";

import { FileName } from "../../components/shared/file-name";
import { formatBytes } from "../../lib/format/bytes";
import type { ProcessingFile } from "../../lib/processing/files-api";
import { plural } from "../../lib/ui/mm-plural";
import { SourceTag } from "./lane-cards";
import type { WorkingItem } from "./processing-model";
import {
  clock,
  readRate,
  runningFor,
  speedWords,
  timeLeft,
} from "./processing-words";

const STEPS = ["Check", "Plan", "Write", "Verify", "Hand back"];
/** The step a pass is on once it is writing; the ones before it are done. */
const WRITING_STEP = 2;
/** The thinnest the progress bar's fill gets, so a pass that has just started still shows. */
const MIN_FILL_PERCENT = 2;

type StepState = "done" | "now" | "next";

function stepState(index: number, writing: boolean): StepState {
  const current = writing ? WRITING_STEP : 0;
  if (index < current) return "done";
  return index === current ? "now" : "next";
}

/** What the pass is doing, in numbers from the server's own progress and the file's size and length. */
function workingStats(item: WorkingItem): [string, string][] {
  const file = item.file;
  const speed = speedWords(item.speed);
  const rate = readRate(
    file?.size_bytes,
    item.percent,
    file?.progress_elapsed_seconds,
  );
  const duration = file?.duration_seconds ?? null;
  const through =
    duration && item.percent != null
      ? `${clock((duration * Math.min(100, item.percent)) / 100)} of ${clock(duration)}`
      : "";
  const elapsed = file?.progress_elapsed_seconds ?? 0;
  const running = elapsed >= 1 ? runningFor(elapsed) : "";
  return [
    speed ? ["Speed", `${speed} real time`] : null,
    rate ? ["Reading", `${formatBytes(rate)}/s`] : null,
    through ? ["Through the file", through] : null,
    running ? ["Running for", running] : null,
  ].filter((stat): stat is [string, string] => stat !== null);
}

function removedTracks(item: WorkingItem): string[] {
  const removed: string[] = [];
  if (item.removedAudio) removed.push(`${item.removedAudio} audio`);
  if (item.removedSubtitles) {
    removed.push(plural(item.removedSubtitles, "subtitle", "subtitles"));
  }
  return removed;
}

function StepList({ writing }: { writing: boolean }) {
  return (
    <ol className="mm-live-steps" aria-label="Steps">
      {STEPS.map((step, index) => {
        const state = stepState(index, writing);
        return (
          <li key={step} className={`mm-live-step mm-live-step--${state}`}>
            {state === "now" && !writing ? "Checking" : step}
            {state === "done" ? <span className="sr-only"> (done)</span> : null}
          </li>
        );
      })}
    </ol>
  );
}

function ProgressBar({
  item,
  writing,
}: {
  item: WorkingItem;
  writing: boolean;
}) {
  return (
    <div
      className={`mm-live-bar${writing ? "" : " mm-live-bar--busy"}`}
      role="progressbar"
      aria-label={`Progress for ${item.name}`}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={writing ? Math.round(item.percent ?? 0) : undefined}
    >
      <span
        className="mm-live-bar__fill"
        style={
          {
            "--mm-live-fill": Math.max(MIN_FILL_PERCENT, item.percent ?? 0),
          } as CSSProperties
        }
      />
    </div>
  );
}

export function WorkingCard({
  item,
  onOpen,
}: {
  item: WorkingItem;
  onOpen: (file: ProcessingFile) => void;
}) {
  const writing = item.percent != null;
  const removed = removedTracks(item);
  const stats = writing ? workingStats(item) : [];
  const file = item.file;
  return (
    <li className="mm-live-card mm-live-card--work" data-testid="live-working">
      <div className="mm-live-work__top">
        <div className="mm-live-card__names">
          <SourceTag source={item.source} libraryName={item.libraryName} />
          <span className="mm-live-card__title mm-live-card__title--lg">
            {file ? (
              <button
                type="button"
                className="mm-live-card__open"
                onClick={() => onOpen(file)}
              >
                {item.name}
              </button>
            ) : (
              item.name
            )}
          </span>
          <FileName path={item.path} className="mm-live-card__file" />
          <span className="mm-live-card__sub">{item.facts}</span>
        </div>
        <div className="mm-live-work__pct">
          <span className="mm-live-work__number">
            {writing ? `${Math.floor(item.percent ?? 0)}%` : ""}
          </span>
          <span className="mm-live-card__sub">
            {writing
              ? timeLeft(item.etaSeconds)
              : item.source === "library"
                ? "Cleaning in place"
                : "Checking the file"}
          </span>
        </div>
      </div>
      <ProgressBar item={item} writing={writing} />
      {item.source === "download" ? <StepList writing={writing} /> : null}
      {stats.length ? (
        <dl className="mm-live-work__stats" data-testid="live-working-stats">
          {stats.map(([label, value]) => (
            <div key={label} className="mm-live-work__stat">
              <dt>{label}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      ) : null}
      {removed.length ? (
        <p className="mm-live-work__facts">
          <span>
            Removing <b>{removed.join(", ")}</b>
          </span>
        </p>
      ) : null}
    </li>
  );
}

import { parseActivityDetail } from "../../lib/activity/detail";
import {
  formatDuration,
  formatProcessingSpeed,
  type FileProgressDetail as Detail,
} from "../../lib/activity/pass-detail";
import { baseName } from "../../lib/format/path";

const STATUS: Record<string, { text: string; modifier: string }> = {
  finished: { text: "Finished", modifier: "finished" },
  failed: { text: "Stopped", modifier: "failed" },
  finishing: { text: "Final checks", modifier: "finishing" },
};

/** A pass that is still running, in plain words: how far, how long, how fast. */
export function FileProgressDetail({ detail }: { detail: string }) {
  const parsed = parseActivityDetail(detail) as Detail | null;

  if (!parsed) {
    return <p className="mm-activity-processing__raw">{detail}</p>;
  }

  const rawPercent =
    typeof parsed.percent === "number" && Number.isFinite(parsed.percent)
      ? parsed.percent
      : null;
  const percent =
    rawPercent == null ? null : Math.max(0, Math.min(100, rawPercent));
  const status = parsed.status || "processing";
  const fileName =
    baseName(
      parsed.relative_media_path || parsed.inspected_source_path || "",
    ) || "this file";
  const eta = formatDuration(parsed.eta_seconds);
  const elapsed = formatDuration(parsed.elapsed_seconds);
  const processed = formatDuration(parsed.processed_seconds);
  const duration = formatDuration(parsed.duration_seconds);
  const speed = formatProcessingSpeed(parsed.speed);
  const { text: statusText, modifier } = STATUS[status] ?? {
    text: "Processing",
    modifier: "processing",
  };
  const message =
    status === "finished"
      ? "Weir finished processing this file."
      : status === "failed"
        ? parsed.message || "Weir could not finish this file."
        : parsed.message || `Weir is processing ${fileName}.`;
  const primaryMetric =
    status === "finished" || status === "failed"
      ? statusText
      : eta
        ? `About ${eta} left`
        : status === "processing"
          ? "Estimating time left"
          : statusText;

  return (
    <div
      className={`mm-activity-processing mm-activity-processing--${modifier}`}
      data-testid="processing-processing-progress-detail"
    >
      <div className="mm-activity-processing__header">
        <div>
          <p className="mm-activity-processing__eyebrow">{statusText}</p>
          <p className="mm-activity-processing__title">{message}</p>
        </div>
        <strong className="mm-activity-processing__percent">
          {percent == null ? "Working" : `${Math.round(percent)}%`}
        </strong>
      </div>
      {/* Without a percentage the bar shows a sliver, and says nothing about how far it is. */}
      <div
        className="mm-activity-processing__bar"
        role="progressbar"
        aria-label={`Processing progress for ${fileName}`}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent == null ? undefined : Math.round(percent)}
      >
        <span style={{ width: `${percent ?? 8}%` }} />
      </div>
      <div className="mm-activity-processing__metrics">
        <span>{primaryMetric}</span>
        {elapsed ? <span>Running {elapsed}</span> : null}
        {processed && duration ? (
          <span>
            Processed {processed} of {duration}
          </span>
        ) : null}
        {speed ? <span>Processing speed {speed}</span> : null}
      </div>
      {parsed.reason ? (
        <p className="mm-activity-processing__error">{parsed.reason}</p>
      ) : null}
    </div>
  );
}

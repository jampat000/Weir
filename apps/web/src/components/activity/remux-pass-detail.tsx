import { parseActivityDetail } from "../../lib/activity/detail";
import {
  cleanupStatus,
  formatSavings,
  outcomeLabel,
  splitTrackList,
  type RemuxPassDetail as Detail,
} from "../../lib/activity/pass-detail";
import { formatBytes } from "../../lib/format/bytes";

function detailRow(label: string, value: string | undefined | null) {
  if (value === undefined || value === null || value === "") return null;
  return (
    <div key={label} className="mm-activity-remux-detail__row">
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function trackSection(
  label: string,
  values: string[],
  tone: "before" | "kept" | "removed",
  emptyLabel = "None",
) {
  return (
    <div key={label} className="mm-activity-remux-detail__tracks-section">
      <h5 className="mm-activity-remux-detail__tracks-title">{label}</h5>
      {values.length === 0 ? (
        <p className="mm-activity-remux-detail__tracks-empty">{emptyLabel}</p>
      ) : (
        <div className="mm-activity-remux-detail__chips">
          {values.map((value) => (
            <span
              key={`${label}-${value}`}
              className={`mm-activity-remux-detail__chip mm-activity-remux-detail__chip--${tone}`}
            >
              {value}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

/** What one finished pass did to a file: sizes up front, tracks and folders behind "Show". */
export function RemuxPassDetail({ detail }: { detail: string }) {
  const parsed = parseActivityDetail(detail) as Detail | null;

  if (!parsed) {
    return (
      <span
        className="mm-activity-row__detail"
        data-testid="processing-remux-activity-detail-raw"
      >
        {detail}
      </span>
    );
  }

  const sourceSize = formatBytes(parsed.source_size_bytes);
  const outputSize = formatBytes(parsed.output_size_bytes);
  const savings = formatSavings(
    parsed.source_size_bytes,
    parsed.output_size_bytes,
  );
  const streamsInspected = parsed.stream_counts
    ? `Video ${parsed.stream_counts.video ?? 0}, audio ${parsed.stream_counts.audio ?? 0}, subtitles ${parsed.stream_counts.subtitle ?? 0}`
    : undefined;

  const summaryTiles = [
    {
      label: "Outcome",
      value: outcomeLabel(parsed.outcome, parsed.pass_through_unchanged),
    },
    { label: "Original size", value: sourceSize },
    { label: "Final size", value: outputSize },
    { label: "Change", value: savings },
  ].filter((row) => row.value);

  const beforeRows = [
    detailRow(
      "File checked",
      parsed.inspected_source_path || parsed.relative_media_path,
    ),
    detailRow("Original size", sourceSize),
    detailRow("Streams inspected", streamsInspected),
  ].filter(Boolean);

  const afterRows = [
    detailRow("Output file", parsed.output_file),
    detailRow("Final size", outputSize),
  ].filter(Boolean);

  const notes: { k: string; v: string | undefined | null }[] = [
    {
      k: "Changes needed",
      v:
        parsed.remux_required === undefined
          ? undefined
          : parsed.remux_required
            ? "Yes"
            : "No",
    },
    { k: "What Weir planned", v: parsed.plan_summary },
    ...cleanupStatus(parsed).map((row) => ({ k: row.label, v: row.value })),
    { k: "Safety note", v: parsed.output_completeness_note || undefined },
    { k: "Reason", v: parsed.reason },
  ];

  return (
    <div
      className="mm-activity-remux-detail"
      data-testid="processing-remux-activity-detail"
    >
      {summaryTiles.length > 0 ? (
        <div className="mm-activity-remux-detail__tiles">
          {summaryTiles.map((tile) => (
            <div key={tile.label} className="mm-activity-remux-detail__tile">
              <span className="mm-activity-remux-detail__tile-label">
                {tile.label}
              </span>
              <span className="mm-activity-remux-detail__tile-value">
                {tile.value}
              </span>
            </div>
          ))}
        </div>
      ) : null}
      <details className="mm-activity-remux-detail__expand">
        <summary>Show track and cleanup details</summary>
        <div className="mm-activity-remux-detail__expand-body">
          <div className="mm-activity-remux-detail__compare">
            <section className="mm-activity-remux-detail__column">
              <h4 className="mm-activity-remux-detail__column-title">Before</h4>
              <dl className="mm-activity-remux-detail__dl">{beforeRows}</dl>
              <div className="mm-activity-remux-detail__tracks">
                {trackSection(
                  "Audio in file",
                  splitTrackList(parsed.audio_before),
                  "before",
                )}
                {trackSection(
                  "Subtitles in file",
                  splitTrackList(parsed.subs_before),
                  "before",
                )}
              </div>
            </section>
            <section className="mm-activity-remux-detail__column">
              <h4 className="mm-activity-remux-detail__column-title">After</h4>
              <dl className="mm-activity-remux-detail__dl">{afterRows}</dl>
              <div className="mm-activity-remux-detail__tracks">
                {trackSection(
                  "Audio kept",
                  splitTrackList(parsed.audio_after),
                  "kept",
                )}
                {trackSection(
                  "Audio removed",
                  parsed.removed_audio ?? [],
                  "removed",
                  "None removed",
                )}
                {trackSection(
                  "Subtitles kept",
                  splitTrackList(parsed.subs_after),
                  "kept",
                )}
                {trackSection(
                  "Subtitles removed",
                  parsed.removed_subtitles ?? [],
                  "removed",
                  "None removed",
                )}
              </div>
            </section>
          </div>

          <div className="mm-activity-remux-detail__supplemental">
            <h4 className="mm-activity-remux-detail__section-title">
              Processing notes
            </h4>
            <dl className="mm-activity-remux-detail__dl">
              {notes
                .filter((r) => r.v !== undefined && r.v !== null && r.v !== "")
                .map((r) => (
                  <div key={r.k} className="mm-activity-remux-detail__row">
                    <dt>{r.k}</dt>
                    <dd>{String(r.v)}</dd>
                  </div>
                ))}
            </dl>
          </div>

          {Array.isArray(parsed.ffmpeg_argv) &&
          parsed.ffmpeg_argv.length > 0 ? (
            <details className="mm-activity-remux-detail__ffmpeg">
              <summary>
                ffmpeg command line
                {parsed.ffmpeg_argv_truncated ? " (truncated in log)" : ""}
              </summary>
              <pre className="mm-activity-remux-detail__pre">
                {parsed.ffmpeg_argv.join(" ")}
              </pre>
            </details>
          ) : null}
        </div>
      </details>
    </div>
  );
}

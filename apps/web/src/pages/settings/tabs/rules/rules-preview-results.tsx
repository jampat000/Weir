import { formatBytes } from "../../../../lib/format/bytes";
import type {
  ProcessingRulesPreviewResult,
  ProcessingRulesPreviewTrack,
} from "../../../../lib/processing/rules-preview-api";
import { mmStatusPillClass } from "../../../../lib/ui/mm-status-tone";

const TRACK_TYPE_LABELS: Record<ProcessingRulesPreviewTrack["type"], string> = {
  video: "Video",
  audio: "Audio",
  subtitle: "Subtitle",
};

const NOTHING = "—";

const TRACK_COLUMNS = [
  "#",
  "Type",
  "Codec",
  "Language",
  "Title",
  "Channels",
  "Action",
  "Flags",
  "Reasons",
];

function flagsOf(track: ProcessingRulesPreviewTrack): string {
  const flags = [
    track.default ? "Default" : null,
    track.forced ? "Forced" : null,
  ];
  return flags.filter(Boolean).join(", ") || NOTHING;
}

function TrackRow({ track }: { track: ProcessingRulesPreviewTrack }) {
  const kept = track.action === "keep";
  return (
    <tr>
      <td data-label="#" className="mm-rules-preview__index">
        {track.index}
      </td>
      <td data-label="Type">{TRACK_TYPE_LABELS[track.type]}</td>
      <td data-label="Codec">{track.codec || NOTHING}</td>
      <td data-label="Language">
        {track.language ? track.language.toUpperCase() : NOTHING}
      </td>
      <td
        data-label="Title"
        className="mm-rules-preview__track-title"
        title={track.title}
      >
        {track.title || NOTHING}
      </td>
      <td data-label="Channels">
        {track.type === "audio" && track.channels > 0
          ? track.channels
          : NOTHING}
      </td>
      <td data-label="Action">
        <span className={mmStatusPillClass(kept ? "healthy" : "failed")}>
          {kept ? "Keep" : "Drop"}
        </span>
      </td>
      <td data-label="Flags" className="mm-rules-preview__small">
        {flagsOf(track)}
      </td>
      <td data-label="Reasons" className="mm-rules-preview__reasons">
        <ul className="list-disc space-y-1 pl-4">
          {track.reasons.map((reason, index) => (
            <li key={index}>{reason}</li>
          ))}
        </ul>
      </td>
    </tr>
  );
}

/** What the rules would do to the previewed file: the verdict, every track, and the container notes. */
export function RulesPreviewResults({
  result,
}: {
  result: ProcessingRulesPreviewResult;
}) {
  const reduction = result.estimated_size_reduction_bytes;
  // A file the plan would make bigger has no "smaller" to state.
  const sizeText =
    reduction != null && reduction >= 0 ? formatBytes(reduction) : "";
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <span
          className={mmStatusPillClass(
            result.remux_required ? "warning" : "healthy",
          )}
        >
          {result.remux_required
            ? "A remux would run"
            : "Already matches — no remux needed"}
        </span>
        {sizeText ? (
          <span className="text-mm-text3">
            Estimated size change: about {sizeText} smaller (estimate only)
          </span>
        ) : null}
      </div>

      <div className="mm-quiet-table-wrap">
        <table className="mm-quiet-table">
          <caption className="sr-only">
            Per-track plan for the previewed file
          </caption>
          <thead>
            <tr>
              {TRACK_COLUMNS.map((heading) => (
                <th key={heading} scope="col">
                  {heading}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {result.tracks.map((track) => (
              <TrackRow key={track.index} track={track} />
            ))}
          </tbody>
        </table>
      </div>

      {result.original_language ? (
        <p className="mm-rules-preview__note">
          Original language: {result.original_language.note}
        </p>
      ) : null}

      {result.metadata_notes.length > 0 ? (
        <div>
          <h4 className="mm-rules-preview__subhead">Container changes</h4>
          <ul className="mm-rules-preview__list mt-1">
            {result.metadata_notes.map((note, index) => (
              <li key={index}>{note}</li>
            ))}
          </ul>
        </div>
      ) : null}

      {result.notes.length > 0 ? (
        <details className="mm-rules-preview__notes">
          <summary className="mm-rules-preview__subhead cursor-pointer">
            Full plan notes ({result.notes.length})
          </summary>
          <ul className="mm-rules-preview__list mt-2">
            {result.notes.map((note, index) => (
              <li key={index}>{note}</li>
            ))}
          </ul>
        </details>
      ) : null}
    </div>
  );
}

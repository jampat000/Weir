import { Fragment, type ReactNode } from "react";

import { Chip } from "../../../../components/panels/chip";
import { SortableColumnHeader } from "../../../../components/shared/sortable-column-header";
import type { ProcessingRulesPreviewTrack } from "../../../../lib/processing/rules-preview-api";
import { sortRows } from "../../../../lib/ui/table-columns";
import { trackMeaning } from "../../../../lib/ui/track-meaning";
import type { TableColumns } from "../../../../lib/ui/use-table-columns";
import {
  PREVIEW_SORT_VALUES,
  flagWords,
  type PreviewColumnId,
} from "./rules-preview-columns";

const TRACK_TYPE_LABELS: Record<ProcessingRulesPreviewTrack["type"], string> = {
  video: "Video",
  audio: "Audio",
  subtitle: "Subtitle",
};

const NOTHING = "—";

function TrackRow({
  track,
  order,
  labels,
}: {
  track: ProcessingRulesPreviewTrack;
  order: readonly PreviewColumnId[];
  /** The heading of each column, shown beside its value once a narrow screen stacks the rows. */
  labels: (id: PreviewColumnId) => string;
}) {
  const kept = track.action === "keep";
  const cells: Record<PreviewColumnId, ReactNode> = {
    index: (
      <td
        data-col="index"
        data-label={labels("index")}
        className="mm-rules-preview__index"
      >
        {track.index}
      </td>
    ),
    type: (
      <td data-col="type" data-label={labels("type")}>
        {TRACK_TYPE_LABELS[track.type]}
      </td>
    ),
    codec: (
      <td data-col="codec" data-label={labels("codec")}>
        {track.codec || NOTHING}
      </td>
    ),
    language: (
      <td data-col="language" data-label={labels("language")}>
        {track.language ? track.language.toUpperCase() : NOTHING}
      </td>
    ),
    title: (
      <td
        data-col="title"
        data-label={labels("title")}
        className="mm-rules-preview__track-title"
        title={track.title}
      >
        {track.title || NOTHING}
      </td>
    ),
    channels: (
      <td data-col="channels" data-label={labels("channels")}>
        {track.type === "audio" && track.channels > 0
          ? track.channels
          : NOTHING}
      </td>
    ),
    action: (
      <td data-col="action" data-label={labels("action")}>
        <Chip meaning={trackMeaning(kept)} dot={false}>
          {kept ? "Keep" : "Drop"}
        </Chip>
      </td>
    ),
    flags: (
      <td
        data-col="flags"
        data-label={labels("flags")}
        className="mm-rules-preview__small"
      >
        {flagWords(track) || NOTHING}
      </td>
    ),
    reasons: (
      <td
        data-col="reasons"
        data-label={labels("reasons")}
        className="mm-rules-preview__reasons"
      >
        <ul className="list-disc space-y-1 pl-4">
          {track.reasons.map((reason, index) => (
            <li key={index}>{reason}</li>
          ))}
        </ul>
      </td>
    ),
  };
  return (
    <tr>
      {order.map((id) => (
        <Fragment key={id}>{cells[id]}</Fragment>
      ))}
    </tr>
  );
}

/** What the rules would do to each track of the previewed file, under headings that sort it and whose columns move. */
export function RulesPreviewTable({
  tracks,
  columns,
}: {
  tracks: readonly ProcessingRulesPreviewTrack[];
  columns: TableColumns<PreviewColumnId>;
}) {
  const labels = (id: PreviewColumnId) => columns.heading(id).column.label;
  return (
    <div className="mm-quiet-table-wrap">
      <table className="mm-quiet-table" {...columns.tableProps}>
        <caption className="sr-only">
          Per-track plan for the previewed file
        </caption>
        <thead>
          <tr>
            {columns.order.map((id) => (
              <SortableColumnHeader key={id} heading={columns.heading(id)} />
            ))}
          </tr>
        </thead>
        <tbody>
          {sortRows(tracks, columns.sort, PREVIEW_SORT_VALUES).map((track) => (
            <TrackRow
              key={track.index}
              track={track}
              order={columns.order}
              labels={labels}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}

import type { ProcessingRulesPreviewTrack } from "../../../../lib/processing/rules-preview-api";
import { trackMeaning } from "../../../../lib/ui/track-meaning";
import {
  meaningRank,
  type TableColumnsConfig,
} from "../../../../lib/ui/table-columns";

export type PreviewColumnId =
  | "index"
  | "type"
  | "codec"
  | "language"
  | "title"
  | "channels"
  | "action"
  | "flags"
  | "reasons";

/**
 * The tracks of a previewed file, in the order the file holds them until a heading sorts them. A different file is a
 * different table, but how the columns were left is kept for the next one.
 */
export const PREVIEW_COLUMNS: TableColumnsConfig<PreviewColumnId> = {
  tableId: "rules-preview-tracks",
  sortable: true,
  defaultSort: null,
  columns: [
    { id: "index", label: "#" },
    { id: "type", label: "Type" },
    { id: "codec", label: "Codec" },
    { id: "language", label: "Language" },
    { id: "title", label: "Title" },
    { id: "channels", label: "Channels", firstDirection: "desc" },
    { id: "action", label: "Action" },
    { id: "flags", label: "Flags" },
    { id: "reasons", label: "Reasons", sortable: false },
  ],
};

const TRACK_TYPE_ORDER: readonly ProcessingRulesPreviewTrack["type"][] = [
  "video",
  "audio",
  "subtitle",
];

/** The flags a track carries as words, or "" for none. */
export function flagWords(track: ProcessingRulesPreviewTrack): string {
  return [track.default ? "Default" : null, track.forced ? "Forced" : null]
    .filter(Boolean)
    .join(", ");
}

/** What each column that sorts sorts by: kinds in the order video, audio, subtitle, and keeps before drops. */
export const PREVIEW_SORT_VALUES = {
  index: (track: ProcessingRulesPreviewTrack) => track.index,
  type: (track: ProcessingRulesPreviewTrack) =>
    TRACK_TYPE_ORDER.indexOf(track.type),
  codec: (track: ProcessingRulesPreviewTrack) => track.codec || null,
  language: (track: ProcessingRulesPreviewTrack) => track.language || null,
  title: (track: ProcessingRulesPreviewTrack) => track.title || null,
  channels: (track: ProcessingRulesPreviewTrack) =>
    track.type === "audio" && track.channels > 0 ? track.channels : null,
  action: (track: ProcessingRulesPreviewTrack) =>
    meaningRank(trackMeaning(track.action === "keep")),
  flags: (track: ProcessingRulesPreviewTrack) => flagWords(track) || null,
};

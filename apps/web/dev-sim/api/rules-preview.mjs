/** "Try these rules on a file": what the rules would keep and drop, shown against one of the workflow's own files. */
import { shaped } from "../openapi/skeleton.mjs";
import { trackStreams } from "./file-details.mjs";

function previewTrack(stream) {
  return {
    index: stream.index,
    type: stream.type,
    codec: stream.codec ?? "",
    language: stream.language ?? "",
    title: stream.title ?? "",
    channels: stream.channels ?? 0,
    default: stream.default,
    forced: stream.forced,
    action: stream.rule_would_keep ? "keep" : "drop",
    reasons: [stream.rule_reason],
  };
}

/**
 * @param {import("../sim.mjs").Sim} sim
 * @param {Record<string, any>} library
 * @param {Record<string, any>} body
 */
export function rulesPreview(sim, library, body) {
  const sample = [...sim.engine.files.values()].find(
    (file) => file.libraryId === library.id,
  );
  const tracks = sample ? trackStreams(sample).map(previewTrack) : [];
  const dropped = tracks.filter((track) => track.action === "drop");
  return shaped("ProcessingRulesPreviewOut", {
    library_id: library.id,
    media_scope: library.media_type,
    inspected_path:
      body.absolute_path ?? body.relative_path ?? sample?.relativePath ?? "",
    remux_required: dropped.length > 0,
    estimated_size_reduction_bytes: sample
      ? Math.round(sample.sizeBytes * dropped.length * 0.02)
      : null,
    estimated_size_reduction_is_estimate: true,
    metadata_notes: [],
    notes: [],
    original_language: null,
    tracks,
  });
}

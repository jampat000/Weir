/** What a single file's detail screens show: its tracks, and its record as a text download. */

const VIDEO_INDEX = 0;
const KEPT_AUDIO_CHANNELS = 6;

function audioStream(index, line, kept) {
  const [language, layout, codec] = line.replace(/ \(.*$/, "").split(" ");
  return {
    index,
    type: "audio",
    codec: codec?.toLowerCase().replace("-", "") ?? null,
    language: language.slice(0, 3).toLowerCase(),
    title: null,
    channels: layout === "5.1" ? KEPT_AUDIO_CHANNELS : 2,
    default: kept,
    forced: false,
    rule_would_keep: kept,
    rule_reason: kept
      ? "English is the audio language the rules keep."
      : "Not an audio language the rules keep.",
  };
}

function subtitleStream(index, language, kept) {
  return {
    index,
    type: "subtitle",
    codec: "subrip",
    language,
    title: null,
    channels: null,
    default: false,
    forced: false,
    rule_would_keep: kept,
    rule_reason: kept
      ? "English subtitles are kept."
      : "Not a subtitle language the rules keep.",
  };
}

/**
 * The tracks a file has, with what the saved rules would do to each.
 * @param {import("../engine/file.mjs").SimFile} file
 */
export function trackStreams(file) {
  const video = {
    index: VIDEO_INDEX,
    type: "video",
    codec: file.video.codec,
    language: null,
    title: null,
    channels: null,
    default: true,
    forced: false,
    rule_would_keep: true,
    rule_reason: "The video is always kept.",
  };
  const kept = audioStream(1, "eng ac3 5.1", true);
  const removed = file.plan.removedAudio.map((line, offset) =>
    audioStream(2 + offset, line.toLowerCase(), false),
  );
  const subtitleBase = 2 + removed.length;
  const english = subtitleStream(subtitleBase, "eng", true);
  const others = file.plan.removedSubtitles.map((line, offset) =>
    subtitleStream(
      subtitleBase + 1 + offset,
      line.slice(0, 3).toLowerCase(),
      false,
    ),
  );
  return [video, kept, ...removed, english, ...others];
}

/**
 * A file's record as the plain text a person attaches to a bug report.
 * @param {{ relative_path: string, entries: { recorded_at: string, outcome: string, story: { heading: string, sentence: string }[] }[] }} log
 */
export function fileLogText(log) {
  const lines = [log.relative_path, ""];
  for (const entry of log.entries) {
    lines.push(`${entry.recorded_at}  ${entry.outcome}`);
    for (const step of entry.story)
      lines.push(`  ${step.heading}: ${step.sentence}`);
  }
  return lines.join("\n");
}

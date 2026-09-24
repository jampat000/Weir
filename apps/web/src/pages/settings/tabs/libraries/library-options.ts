import {
  PROCESSING_MEDIA_TYPE_LABELS,
  type ProcessingFailurePolicy,
  type ProcessingMediaType,
  type RemuxWriter,
} from "../../../../lib/processing/libraries-api";
import type { SettingOption } from "./library-settings";

const MEDIA_TYPES: ProcessingMediaType[] = ["movie", "tv"];

export const MEDIA_TYPE_OPTIONS: SettingOption[] = MEDIA_TYPES.map((type) => ({
  value: type,
  label: PROCESSING_MEDIA_TYPE_LABELS[type],
}));

export const WRITER_OPTIONS: SettingOption[] = [
  { value: "best", label: "The best tool for each file" },
  { value: "ffmpeg", label: "FFmpeg only" },
];

export const WRITER_HINTS: Record<RemuxWriter, string> = {
  best: "mkvmerge writes MKV files when it is installed, FFmpeg writes the rest, and FFmpeg writes again if mkvmerge's copy fails Weir's checks.",
  ffmpeg: "FFmpeg writes every file, as every Weir before 3.0 did.",
};

export const REJECTED_FILE_OPTIONS: SettingOption[] = [
  { value: "leave", label: "Leave the file in place" },
  { value: "delete_file", label: "Delete only the rejected file" },
];

export const COLLISION_OPTIONS: SettingOption[] = [
  { value: "replace", label: "Replace it" },
  { value: "skip", label: "Keep it and skip this file" },
  { value: "keep_both", label: "Keep both" },
  { value: "replace_if_larger", label: "Replace only if new output is larger" },
  { value: "replace_if_newer", label: "Replace only if source is newer" },
];

/** The most files one library can be held to at once from its editor. */
const MOST_FILES_AT_ONCE = 10;

export const FILES_AT_ONCE_OPTIONS: SettingOption[] = [
  { value: "0", label: "Same as Process settings" },
  ...Array.from({ length: MOST_FILES_AT_ONCE }, (_, i) => ({
    value: String(i + 1),
    label: i === 0 ? "At most 1 file" : `At most ${i + 1} files`,
  })),
];

export const HARDWARE_DECODE_OPTIONS: SettingOption[] = [
  { value: "off", label: "Off" },
  { value: "auto", label: "Detect automatically" },
  { value: "device", label: "Use a specific method" },
];

export const STRICTNESS_OPTIONS: SettingOption[] = [
  { value: "very", label: "Very strict" },
  { value: "strict", label: "Strict" },
  { value: "normal", label: "Normal" },
  { value: "unofficial", label: "Allow unofficial" },
  { value: "experimental", label: "Allow experimental" },
];

export const FAILURE_POLICY_HINTS: Record<ProcessingFailurePolicy, string> = {
  pass_through:
    "Your media manager still gets the file, exactly as it arrived. The original stays in the watched folder.",
  hold: "The file stays with Weir and will not reach your media manager until you deal with it.",
  reject:
    "Weir tells your media manager the release is bad and removes the download once the manager accepts, so it can find a different one. If that cannot be done safely, the original is handed back unchanged instead.",
};

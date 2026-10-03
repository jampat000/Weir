/** What went wrong with a file that stopped or was turned away, in the few words a card or a group says it in. */
import {
  REJECTED_BY_RULES,
  type ProcessingFile,
} from "../../lib/processing/files-api";
import { holdWords } from "./reason-words";

/** Said when a file stopped and nothing more specific is known: the original is safe either way. */
const COULDNT_FINISH = "Couldn't finish · original kept";

export type FileReason = {
  key: string;
  /** What follows a count in the group's title: "3 not in a language you keep". */
  words: string;
  /** What a row says under the file's name. */
  short: string;
  rejected: boolean;
};

/** What the rules say when the audio left nothing in a language the workflow keeps. */
const LANGUAGE_REJECTION = /language|audio tracks/i;

/** What went wrong with a file: the words a group is titled with, and the few a row says. */
export function fileReason(file: ProcessingFile): FileReason {
  if (file.status === "rejected") {
    if (LANGUAGE_REJECTION.test(file.status_reason)) {
      return {
        key: "rejected-language",
        words: "not in a language you keep",
        short: "No audio you keep",
        rejected: true,
      };
    }
    return file.failure_class === REJECTED_BY_RULES
      ? {
          key: "rejected-rules",
          words: "rejected by your rules",
          short: "Rejected",
          rejected: true,
        }
      : {
          key: "rejected-replacement",
          words: "rejected for a replacement",
          short: "Rejected",
          rejected: true,
        };
  }
  if (file.status === "on_hold") {
    return {
      key: "stuck",
      words: "stuck",
      short: holdWords(file.status_reason),
      rejected: false,
    };
  }
  if (file.status === "skipped") {
    return {
      key: "skipped-by-rule",
      words: "skipped by a rule",
      short: "Excluded by a rule",
      rejected: false,
    };
  }
  switch (file.failure_class) {
    case "execution":
      return {
        key: "failed-writing",
        words: "couldn't finish writing",
        short: "Writing stopped · original kept",
        rejected: false,
      };
    case "preflight":
      return {
        key: "failed-checks",
        words: "didn't pass the checks",
        short: "A check failed · original kept",
        rejected: false,
      };
    case "guardrail":
      return {
        key: "failed-guardrail",
        words: "stopped by a safety check",
        short: "Safety stop · original kept",
        rejected: false,
      };
    default:
      return {
        key: "failed",
        words: "couldn't finish",
        short: COULDNT_FINISH,
        rejected: false,
      };
  }
}

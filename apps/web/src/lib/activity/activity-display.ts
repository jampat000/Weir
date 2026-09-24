/**
 * How one Activity entry reads in the log: a title, a one-line summary, a tone and, when it says more
 * than the title, a badge. Pure, so the log and anything else that lists entries agree.
 */
import type { ActivityEventItem } from "../api/types";
import { baseName } from "../format/path";
import { asBoolean, asNumber, asString, parseActivityDetail } from "./detail";
import { FILE_PROGRESS_EVENT, REMUX_PASS_COMPLETED_EVENT } from "./event-types";

export type ActivityTone = "info" | "success" | "warning" | "error";

export type ActivityDisplay = {
  title: string;
  summary: string;
  detail: string | null;
  /** A status badge, only when it says more than the title does. */
  chip: string | null;
  tone: ActivityTone;
  compact: boolean;
};

const EVENT_LABELS: Record<string, string> = {
  "auth.login_succeeded": "Sign-in finished",
  "auth.login_failed": "Sign-in failed",
  "auth.logout": "Sign-out finished",
  "auth.bootstrap_succeeded": "First admin created",
  "auth.bootstrap_denied": "First-time setup blocked",
  "auth.password_changed": "Password changed",
  "auth.username_changed": "Username changed",
  "system.reconciliation.repair": "System repair finished",
  "arr_library.connection_test_succeeded": "Connection check finished",
  "arr_library.connection_test_failed": "Connection check failed",
  "processing.supplied_payload_evaluation_completed":
    "Manual queue check finished",
  "processing.candidate_gate_completed": "Queue check finished",
  "processing.file_processing_progress": "File processing",
  "processing.file_remux_pass_completed": "File processing finished",
  "processing.work_temp_stale_sweep_completed":
    "Temporary files cleanup finished",
  "processing.failure_cleanup_sweep_completed": "Failed-remux cleanup finished",
  "processing.handback_outcome": "What became of a hand-back",
  "processing.unclaimed_handback_cleanup_completed":
    "Unclaimed hand-backs cleanup finished",
};

/** Routine processing events: one fixed title, a compact row. */
const ROUTINE_PROCESSING: Record<string, { title: string; summary: string }> = {
  "processing.supplied_payload_evaluation_completed": {
    title: "Manual queue check finished",
    summary: "Download queue safety check",
  },
  "processing.candidate_gate_completed": {
    title: "Queue check finished",
    summary: "Download queue safety check",
  },
  "processing.work_temp_stale_sweep_completed": {
    title: "Temporary files cleanup finished",
    summary: "Background cleanup result",
  },
  "processing.failure_cleanup_sweep_completed": {
    title: "Failed-remux cleanup finished",
    summary: "Background cleanup result",
  },
};

/** An event type as a person reads it; an unknown one falls back to its last part, in words. */
export function eventLabel(eventType: string): string {
  return (
    EVENT_LABELS[eventType] ??
    eventType.split(".").slice(-1)[0].replaceAll("_", " ")
  );
}

/** Long titles keep their start and their end, which is where a file name differs. */
export function compactActivityTitle(text: string, maxLength = 92): string {
  const normalized = text.trim();
  if (normalized.length <= maxLength) {
    return normalized;
  }
  const tail = Math.max(14, Math.min(28, maxLength - 24));
  const head = Math.max(20, maxLength - tail - 3);
  return `${normalized.slice(0, head).trimEnd()}...${normalized.slice(-tail).trimStart()}`;
}

function progressDisplay(ev: ActivityEventItem): ActivityDisplay {
  const parsed = parseActivityDetail(ev.detail);
  const status = asString(parsed?.status);
  const percent = asNumber(parsed?.percent);
  const name = baseName(asString(parsed?.relative_media_path) ?? "") || "file";
  return {
    title:
      status === "finished"
        ? `${name} finished processing`
        : status === "failed"
          ? `${name} could not be processed`
          : `Processing ${name}`,
    summary:
      percent == null
        ? "Preparing the cleaned-up file"
        : `Writing the cleaned-up file (${Math.round(percent)}%)`,
    detail: ev.detail ?? null,
    chip:
      status === "failed"
        ? "Processing stopped"
        : status === "finished"
          ? "Processing finished"
          : "Processing now",
    tone:
      status === "failed"
        ? "error"
        : status === "finished"
          ? "success"
          : "info",
    compact: false,
  };
}

function passDisplay(ev: ActivityEventItem): ActivityDisplay {
  const parsed = parseActivityDetail(ev.detail);
  const outcome = asString(parsed?.outcome);
  const remuxNeeded = asBoolean(parsed?.remux_required);
  const passedThrough = asBoolean(parsed?.pass_through_unchanged) === true;
  const noChanges = outcome === "live_skipped_not_required";
  const failed = outcome?.startsWith("failed") ?? false;
  const fileName =
    baseName(asString(parsed?.relative_media_path) ?? "") ||
    baseName(asString(parsed?.inspected_source_path) ?? "") ||
    "File";
  return {
    title: passedThrough
      ? `${fileName} was passed through unchanged`
      : noChanges
        ? `No changes needed for ${fileName}`
        : failed
          ? `${fileName} could not be processed`
          : `${fileName} was processed successfully`,
    summary: passedThrough
      ? "Handed back without applying your rules"
      : noChanges
        ? "No changes were needed"
        : failed
          ? "Weir could not finish this file"
          : remuxNeeded === false
            ? "The file already fits your rules"
            : "Cleaned-up file written",
    detail: ev.detail ?? null,
    chip: passedThrough
      ? "Passed through"
      : noChanges
        ? "No changes needed"
        : failed
          ? "Processing failed"
          : "File processed",
    tone: ev.detail?.includes('"ok":false') ? "error" : "success",
    compact: false,
  };
}

function authDisplay(ev: ActivityEventItem): ActivityDisplay {
  return {
    title: eventLabel(ev.event_type),
    summary: ev.module.startsWith("arr_library")
      ? "Service connection check"
      : "Account and sign-in activity",
    detail: ev.detail ?? null,
    chip: null,
    tone:
      ev.event_type.includes("failed") || ev.event_type.includes("denied")
        ? "warning"
        : ev.event_type.includes("succeeded") ||
            ev.event_type.includes("changed")
          ? "success"
          : "info",
    compact: false,
  };
}

/** How one entry reads in the log. */
export function eventDisplay(ev: ActivityEventItem): ActivityDisplay {
  if (ev.event_type === FILE_PROGRESS_EVENT) return progressDisplay(ev);
  if (ev.event_type === REMUX_PASS_COMPLETED_EVENT) return passDisplay(ev);
  const routine = ROUTINE_PROCESSING[ev.event_type];
  if (routine) {
    return {
      ...routine,
      detail: ev.detail ?? null,
      chip: null,
      tone: "success",
      compact: true,
    };
  }
  if (ev.module.startsWith("auth") || ev.module.startsWith("arr_library")) {
    return authDisplay(ev);
  }

  const lowered = `${ev.title} ${ev.detail ?? ""}`.toLowerCase();
  const tone: ActivityTone = /(error|failed|denied)/.test(lowered)
    ? "error"
    : /(skip|missing|review|warning|not configured|unsupported)/.test(lowered)
      ? "warning"
      : /(completed|finished|saved|updated|started)/.test(lowered)
        ? "success"
        : "info";
  return {
    title: eventLabel(ev.event_type),
    summary: ev.module === "processing" ? "Processing" : "System",
    detail: ev.detail ?? null,
    chip: null,
    tone,
    compact: Boolean(ev.detail && ev.detail.length > 120),
  };
}

/** Short plain-text detail (a username, a one-line reason) reads best on the summary line. */
export function inlineDetailOf(
  ev: ActivityEventItem,
  display: ActivityDisplay,
): string | null {
  if (!display.detail || display.compact) return null;
  if (
    ev.event_type === FILE_PROGRESS_EVENT ||
    ev.event_type === REMUX_PASS_COMPLETED_EVENT
  ) {
    return null;
  }
  return parseActivityDetail(display.detail) ? null : display.detail;
}

/** The event types present in a list, labelled and sorted, for the Event filter. */
export function eventOptions(
  items: ActivityEventItem[],
): { value: string; label: string }[] {
  const seen = new Map<string, string>();
  for (const item of items) {
    if (!seen.has(item.event_type)) {
      seen.set(item.event_type, eventLabel(item.event_type));
    }
  }
  return Array.from(seen.entries())
    .map(([value, label]) => ({ value, label }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

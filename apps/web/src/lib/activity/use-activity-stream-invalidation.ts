import { useEffect } from "react";
import { useQueryClient, type QueryKey } from "@tanstack/react-query";
import { useSyncExternalStore } from "react";

type LatestPayload = { latest_event_id: number; activity_revision?: number };
type ActivityLatestSubscriber = () => void;
type LiveProgressSubscriber = () => void;

/**
 * Never cancel a query that is already mid-flight just because a newer activity event arrived: the
 * in-flight answer is still good, and cancelling it only to refetch the same data again wastes a
 * request every time events arrive faster than a screen's own query can finish (#710).
 */
const INVALIDATE_OPTIONS = { cancelRefetch: false } as const;

/** How far a running pass has got, straight from the `processing.progress` stream frame (#750). */
export type LiveProgressEntry = {
  relativePath: string;
  status: string;
  percent: number | null;
  etaSeconds: number | null;
  message: string | null;
  speed: string | null;
  elapsedSeconds: number | null;
  removedAudio: string[];
  removedSubtitles: string[];
};

let source: EventSource | null = null;
let lastSeen: LatestPayload | null = null;
const subscribers = new Set<ActivityLatestSubscriber>();

const EMPTY_PROGRESS: Readonly<Record<string, LiveProgressEntry>> = {};
let liveProgressByPath: Readonly<Record<string, LiveProgressEntry>> =
  EMPTY_PROGRESS;
const progressSubscribers = new Set<LiveProgressSubscriber>();

function emitActivityLatest(): void {
  subscribers.forEach((subscriber) => subscriber());
}

function parseLatestPayload(data: string): LatestPayload | null {
  try {
    const parsed = JSON.parse(data) as Partial<LatestPayload>;
    if (typeof parsed.latest_event_id !== "number") {
      return null;
    }
    return {
      latest_event_id: parsed.latest_event_id,
      activity_revision:
        typeof parsed.activity_revision === "number"
          ? parsed.activity_revision
          : undefined,
    };
  } catch {
    return null;
  }
}

/**
 * Whether `payload` is something this connection hasn't already acted on: a higher event id, or the
 * same event id with a higher revision (an existing row changing in place). A stream replaying its
 * last message on reconnect, or two listeners inside one browser tab racing the same message, must
 * not invalidate every subscribed query a second time (#710).
 */
function isNewActivity(payload: LatestPayload): boolean {
  if (!lastSeen) return true;
  if (payload.latest_event_id !== lastSeen.latest_event_id) {
    return payload.latest_event_id > lastSeen.latest_event_id;
  }
  return (payload.activity_revision ?? 0) > (lastSeen.activity_revision ?? 0);
}

type RawProgressEntry = {
  relative_path?: unknown;
  status?: unknown;
  percent?: unknown;
  eta_seconds?: unknown;
  message?: unknown;
  speed?: unknown;
  elapsed_seconds?: unknown;
  removed_audio?: unknown;
  removed_subtitles?: unknown;
};

function number(value: unknown): number | null {
  return typeof value === "number" ? value : null;
}

function text(value: unknown): string | null {
  return typeof value === "string" ? value : null;
}

function strings(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((v) => typeof v === "string") : [];
}

/** The whole live-progress snapshot the stream carries every frame — never a diff (#750). */
function parseProgressPayload(data: string): LiveProgressEntry[] | null {
  try {
    const parsed = JSON.parse(data) as { files?: unknown };
    if (!Array.isArray(parsed.files)) return null;
    return (parsed.files as RawProgressEntry[]).flatMap((raw) => {
      const relativePath = text(raw.relative_path);
      if (relativePath === null) return [];
      return [
        {
          relativePath,
          status: text(raw.status) ?? "processing",
          percent: number(raw.percent),
          etaSeconds: number(raw.eta_seconds),
          message: text(raw.message),
          speed: text(raw.speed),
          elapsedSeconds: number(raw.elapsed_seconds),
          removedAudio: strings(raw.removed_audio),
          removedSubtitles: strings(raw.removed_subtitles),
        },
      ];
    });
  } catch {
    return null;
  }
}

function closeActivityStream(): void {
  source?.close();
  source = null;
}

/** Stop the connection while the tab is hidden, and pick it back up once anyone still wants it. */
function onVisibilityChange(): void {
  if (document.visibilityState === "hidden") {
    closeActivityStream();
    return;
  }
  if (subscribers.size > 0 || progressSubscribers.size > 0) {
    ensureActivityStream();
  }
}

let watchingVisibility = false;

function watchVisibility(): void {
  if (watchingVisibility || typeof document === "undefined") return;
  document.addEventListener("visibilitychange", onVisibilityChange);
  watchingVisibility = true;
}

function ensureActivityStream(): EventSource | null {
  if (source) {
    return source;
  }
  if (typeof EventSource === "undefined") {
    return null;
  }
  if (
    typeof document !== "undefined" &&
    document.visibilityState === "hidden"
  ) {
    return null;
  }
  source = new EventSource("/api/v1/activity/stream");
  source.addEventListener("activity.latest", (ev) => {
    const payload = parseLatestPayload((ev as MessageEvent<string>).data);
    if (!payload || !isNewActivity(payload)) {
      return;
    }
    lastSeen = payload;
    emitActivityLatest();
  });
  source.addEventListener("processing.progress", (ev) => {
    const entries = parseProgressPayload((ev as MessageEvent<string>).data);
    if (!entries) return;
    const next: Record<string, LiveProgressEntry> = {};
    for (const entry of entries) next[entry.relativePath] = entry;
    liveProgressByPath = next;
    progressSubscribers.forEach((subscriber) => subscriber());
  });
  return source;
}

/** Closes the shared connection once nobody — invalidation or live progress — still wants it. */
function closeIfNobodyIsWatching(): void {
  if (subscribers.size === 0 && progressSubscribers.size === 0) {
    closeActivityStream();
    lastSeen = null;
    liveProgressByPath = EMPTY_PROGRESS;
  }
}

function subscribeActivityLatest(
  subscriber: ActivityLatestSubscriber,
): () => void {
  subscribers.add(subscriber);
  watchVisibility();
  ensureActivityStream();

  return () => {
    subscribers.delete(subscriber);
    closeIfNobodyIsWatching();
  };
}

function subscribeLiveProgress(subscriber: LiveProgressSubscriber): () => void {
  progressSubscribers.add(subscriber);
  watchVisibility();
  ensureActivityStream();

  return () => {
    progressSubscribers.delete(subscriber);
    closeIfNobodyIsWatching();
  };
}

function getLiveProgressSnapshot(): Readonly<
  Record<string, LiveProgressEntry>
> {
  return liveProgressByPath;
}

/**
 * Every file's live progress, straight from the shared `processing.progress` stream frame, updated at
 * most once a second (#750). Empty for a file that is not being worked on, or once its pass ends: the
 * snapshot the stream sends is the whole set in progress, not an accumulating log.
 */
export function useLiveProgress(): Readonly<Record<string, LiveProgressEntry>> {
  return useSyncExternalStore(
    subscribeLiveProgress,
    getLiveProgressSnapshot,
    getLiveProgressSnapshot,
  );
}

export function useActivityStreamInvalidations(
  queryKeys: readonly QueryKey[],
  options: { exact?: boolean; throttleMs?: number } = {},
): void {
  const qc = useQueryClient();
  const exact = options.exact ?? false;
  const throttleMs = Math.max(0, options.throttleMs ?? 0);

  useEffect(() => {
    let lastRunAt = 0;
    let trailingTimer: number | null = null;
    let trailingPending = false;

    const invalidate = () => {
      lastRunAt = Date.now();
      trailingPending = false;
      queryKeys.forEach((queryKey) => {
        void qc.invalidateQueries({ queryKey, exact }, INVALIDATE_OPTIONS);
      });
    };

    const unsubscribe = subscribeActivityLatest(() => {
      const remaining = throttleMs - (Date.now() - lastRunAt);
      if (remaining <= 0) {
        if (trailingTimer !== null) {
          window.clearTimeout(trailingTimer);
          trailingTimer = null;
        }
        invalidate();
        return;
      }

      trailingPending = true;
      if (trailingTimer === null) {
        trailingTimer = window.setTimeout(() => {
          trailingTimer = null;
          if (trailingPending) invalidate();
        }, remaining);
      }
    });

    return () => {
      unsubscribe();
      if (trailingTimer !== null) window.clearTimeout(trailingTimer);
    };
  }, [exact, qc, queryKeys, throttleMs]);
}

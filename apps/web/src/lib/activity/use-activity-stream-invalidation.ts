import { useEffect } from "react";
import { useQueryClient, type QueryKey } from "@tanstack/react-query";

type LatestPayload = { latest_event_id: number; activity_revision?: number };
type ActivityLatestSubscriber = () => void;

/**
 * Never cancel a query that is already mid-flight just because a newer activity event arrived: the
 * in-flight answer is still good, and cancelling it only to refetch the same data again wastes a
 * request every time events arrive faster than a screen's own query can finish (#710).
 */
const INVALIDATE_OPTIONS = { cancelRefetch: false } as const;

let source: EventSource | null = null;
let lastSeen: LatestPayload | null = null;
const subscribers = new Set<ActivityLatestSubscriber>();

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

function closeActivityStream(): void {
  source?.close();
  source = null;
}

/** Stop the connection while the tab is hidden, and pick it back up once it can be seen again. */
function onVisibilityChange(): void {
  if (document.visibilityState === "hidden") {
    closeActivityStream();
    return;
  }
  if (subscribers.size > 0) {
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
  return source;
}

function subscribeActivityLatest(
  subscriber: ActivityLatestSubscriber,
): () => void {
  subscribers.add(subscriber);
  watchVisibility();
  ensureActivityStream();

  return () => {
    subscribers.delete(subscriber);
    if (subscribers.size === 0) {
      closeActivityStream();
      lastSeen = null;
    }
  };
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

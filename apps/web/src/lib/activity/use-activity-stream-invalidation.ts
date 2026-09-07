import { useEffect } from "react";
import { useQueryClient, type QueryKey } from "@tanstack/react-query";

type LatestPayload = { latest_event_id: number; activity_revision?: number };
type ActivityLatestSubscriber = () => void;

let source: EventSource | null = null;
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
    return { latest_event_id: parsed.latest_event_id };
  } catch {
    return null;
  }
}

function ensureActivityStream(): EventSource | null {
  if (source) {
    return source;
  }
  if (typeof EventSource === "undefined") {
    return null;
  }
  source = new EventSource("/api/v1/activity/stream");
  source.addEventListener("activity.latest", (ev) => {
    const payload = parseLatestPayload((ev as MessageEvent<string>).data);
    if (!payload) {
      return;
    }
    emitActivityLatest();
  });
  return source;
}

function subscribeActivityLatest(
  subscriber: ActivityLatestSubscriber,
): () => void {
  subscribers.add(subscriber);
  ensureActivityStream();

  return () => {
    subscribers.delete(subscriber);
    if (subscribers.size === 0) {
      source?.close();
      source = null;
    }
  };
}

export function useActivityStreamInvalidation(queryKey: QueryKey): void {
  const qc = useQueryClient();

  useEffect(() => {
    return subscribeActivityLatest(() => {
      void qc.invalidateQueries({ queryKey });
    });
  }, [qc, queryKey]);
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
        void qc.invalidateQueries({ queryKey, exact });
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

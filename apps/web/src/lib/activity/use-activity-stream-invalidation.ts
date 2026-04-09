import { useEffect } from "react";
import { useQueryClient, type QueryKey } from "@tanstack/react-query";

type LatestPayload = { latest_event_id: number };

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

export function useActivityStreamInvalidation(queryKey: QueryKey): void {
  const qc = useQueryClient();

  useEffect(() => {
    const es = new EventSource("/api/v1/activity/stream");

    const onLatest = (ev: MessageEvent<string>) => {
      const payload = parseLatestPayload(ev.data);
      if (!payload) {
        return;
      }
      void qc.invalidateQueries({ queryKey });
    };

    es.addEventListener("activity.latest", onLatest as EventListener);
    return () => {
      es.removeEventListener("activity.latest", onLatest as EventListener);
      es.close();
    };
  }, [qc, queryKey]);
}

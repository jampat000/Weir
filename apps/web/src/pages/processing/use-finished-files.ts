import { useEffect, useMemo, useRef, useState } from "react";

import {
  LIBRARY_FILE_CLEANED_EVENT,
  REMUX_PASS_COMPLETED_EVENT,
} from "../../lib/activity/event-types";
import {
  finishedFileFromEvent,
  type FinishedFile,
} from "../../lib/activity/processing-outcome";
import { useActivityRecentQuery } from "../../lib/activity/queries";
import { prettyName } from "./processing-model";
import { finishedLine } from "./processing-words";

const RECENT_PASSES = 24;
const RECENT_CLEANS = 12;

/** The newest finished files of both kinds, from the Activity entry each one wrote. */
export function useFinishedFiles(): FinishedFile[] {
  // A download whose title the owner has since removed from History drops out here; a library clean has no
  // such removal, and library files are not in known_files_only's reckoning, so its query never asks for it.
  const passes = useActivityRecentQuery({
    limit: RECENT_PASSES,
    event_type: REMUX_PASS_COMPLETED_EVENT,
    known_files_only: true,
  });
  const cleans = useActivityRecentQuery({
    limit: RECENT_CLEANS,
    event_type: LIBRARY_FILE_CLEANED_EVENT,
  });
  return useMemo(() => {
    const all = [...(passes.data?.items ?? []), ...(cleans.data?.items ?? [])]
      .map(finishedFileFromEvent)
      .filter((item): item is FinishedFile => item !== null);
    return all.sort((a, b) => b.finishedAt.localeCompare(a.finishedAt));
  }, [passes.data, cleans.data]);
}

/**
 * "{name} finished: {summary}" for a screen reader, the moment a new file lands here. Nothing is
 * announced for the files already on screen when the page itself first loads.
 */
export function useFinishedAnnouncement(finished: FinishedFile[]): string {
  const [announcement, setAnnouncement] = useState("");
  const lastId = useRef<number | null>(null);
  const initialized = useRef(false);
  useEffect(() => {
    const newest = finished[0] ?? null;
    if (!initialized.current) {
      initialized.current = true;
      lastId.current = newest?.id ?? null;
      return;
    }
    if (!newest || newest.id === lastId.current) return;
    lastId.current = newest.id;
    setAnnouncement(
      `${prettyName(newest.relativePath)} finished: ${finishedLine(newest)}`,
    );
  }, [finished]);
  return announcement;
}

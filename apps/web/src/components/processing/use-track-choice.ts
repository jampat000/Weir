import { useEffect, useState } from "react";

import type {
  ProcessingFileTrack,
  ProcessingFileTracks,
  ProcessingManualPlanChoice,
} from "../../lib/processing/files-api";

type SelectableType = "video" | "audio" | "subtitle";

function isSelectable(
  type: ProcessingFileTrack["type"],
): type is SelectableType {
  return type === "video" || type === "audio" || type === "subtitle";
}

type RowState = {
  keep: boolean;
  default: boolean;
  forced: boolean;
};

/** Seeded from what the saved rules would have done, so the starting point is never empty. */
function initialRowState(track: ProcessingFileTrack): RowState {
  return {
    keep: track.rule_would_keep,
    default: track.rule_would_keep && track.default,
    forced: track.rule_would_keep && track.forced,
  };
}

export function trackLabel(track: ProcessingFileTrack): string {
  const bits: string[] = [];
  if (track.language) bits.push(track.language);
  if (track.codec) bits.push(track.codec);
  if (track.channels) bits.push(`${track.channels} ch`);
  if (track.title) bits.push(`"${track.title}"`);
  const detail = bits.length > 0 ? bits.join(" · ") : "no tags";
  return `#${track.index} ${track.type} (${detail})`;
}

function validationOf(kept: ProcessingFileTrack[]): string | null {
  if (!kept.some((track) => track.type === "video")) {
    return "Keep at least one video track.";
  }
  if (!kept.some((track) => track.type === "audio")) {
    return "Keep at least one audio track.";
  }
  return null;
}

/**
 * Which tracks to keep, their default and forced flags, and their order. Reset from a fresh set of
 * tracks: a new open, or a reload after "choose again".
 */
export function useTrackChoice(tracks: ProcessingFileTracks | undefined) {
  const [rows, setRows] = useState<Record<number, RowState>>({});
  const [order, setOrder] = useState<number[]>([]);

  useEffect(() => {
    if (!tracks) return;
    const nextRows: Record<number, RowState> = {};
    const kept: number[] = [];
    for (const track of tracks.streams) {
      if (!isSelectable(track.type)) continue;
      const state = initialRowState(track);
      nextRows[track.index] = state;
      if (state.keep) kept.push(track.index);
    }
    setRows(nextRows);
    setOrder(kept);
  }, [tracks]);

  const streams = tracks?.streams ?? [];
  const selectable = streams.filter((track) => isSelectable(track.type));
  const other = streams.filter((track) => !isSelectable(track.type));
  const keptTracks = order
    .map((index) => selectable.find((track) => track.index === index))
    .filter((track): track is ProcessingFileTrack => track !== undefined);

  const setKeep = (track: ProcessingFileTrack, keep: boolean) => {
    setRows((previous) => ({
      ...previous,
      [track.index]: { ...previous[track.index], keep },
    }));
    setOrder((previous) => {
      if (!keep) return previous.filter((index) => index !== track.index);
      return previous.includes(track.index)
        ? previous
        : [...previous, track.index];
    });
  };

  const setDefault = (type: SelectableType, index: number) => {
    setRows((previous) => {
      const next = { ...previous };
      for (const track of selectable) {
        if (track.type !== type) continue;
        next[track.index] = {
          ...next[track.index],
          default: track.index === index,
        };
      }
      return next;
    });
  };

  const setForced = (index: number, forced: boolean) => {
    setRows((previous) => ({
      ...previous,
      [index]: { ...previous[index], forced },
    }));
  };

  const move = (index: number, direction: -1 | 1) => {
    setOrder((previous) => {
      const position = previous.indexOf(index);
      const target = position + direction;
      if (position < 0 || target < 0 || target >= previous.length) {
        return previous;
      }
      const next = [...previous];
      [next[position], next[target]] = [next[target], next[position]];
      return next;
    });
  };

  // Default and forced only mean anything for audio and subtitle tracks; a video track always
  // submits false for both, whatever its own disposition on the source said.
  const choice = (): ProcessingManualPlanChoice => ({
    keep: order.map((index) => {
      const type = selectable.find((track) => track.index === index)?.type;
      return {
        index,
        default: type === "video" ? false : (rows[index]?.default ?? false),
        forced: type === "video" ? false : (rows[index]?.forced ?? false),
      };
    }),
    order,
  });

  return {
    rows,
    selectable,
    other,
    keptTracks,
    dropped: selectable.filter((track) => !(rows[track.index]?.keep ?? false)),
    validationError: validationOf(keptTracks),
    setKeep,
    setDefault,
    setForced,
    move,
    choice,
  };
}

export type TrackChoice = ReturnType<typeof useTrackChoice>;

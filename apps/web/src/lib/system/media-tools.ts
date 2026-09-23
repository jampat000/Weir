import { useQuery } from "@tanstack/react-query";

import { apiFetch, readJson, requireOk } from "../api/client";
import type { components } from "../api/generated/openapi-types";

export type MediaTools = components["schemas"]["MediaToolsOut"];

const mediaToolsPath = "/api/v1/system/media-tools";

/** The ffmpeg and mkvmerge Weir found, as their own version lines. */
export async function fetchMediaTools(): Promise<MediaTools> {
  const response = await apiFetch(mediaToolsPath);
  await requireOk(mediaToolsPath, response, "Could not read Weir's tools");
  return readJson<MediaTools>(response);
}

export function useMediaToolsQuery() {
  return useQuery({
    queryKey: ["system", "media-tools"],
    queryFn: fetchMediaTools,
    staleTime: 5 * 60_000,
  });
}

/**
 * A tool's version line in a few words: "ffmpeg version 7.1-full_build-www.gyan.dev Copyright…" reads "7.1".
 * "not installed" and "unknown" are the server's own answers and pass through.
 */
export function toolVersion(line: string | undefined): string {
  const text = (line ?? "").trim();
  if (!text) return "—";
  // "ffmpeg version 7.1-full_build…", "ffmpeg version n9.0.1-9-gfa97…", "mkvmerge v88.0 ('Æon') 64-bit".
  const match = text.match(/(?:version\s+|\bv)[nv]?([0-9]+(?:\.[0-9]+)*)/i);
  if (match) return match[1];
  return text.length > 40 ? `${text.slice(0, 40)}…` : text;
}

import type { MediaManagerKind } from "../../lib/media-managers/media-managers-api";

/** How the person says downloads reach Weir: the first question of setup. */
export type DownloadSource = "deluno" | "arr" | "client" | "neither";

/** The source that has Weir connect to something and learn folders from it. */
export type ConnectedSource = Exclude<DownloadSource, "neither">;

export const DOWNLOAD_SOURCE_OPTIONS: {
  value: DownloadSource;
  label: string;
}[] = [
  { value: "deluno", label: "Deluno" },
  { value: "arr", label: "Sonarr / Radarr" },
  {
    value: "client",
    label: "A download client (SABnzbd, qBittorrent, …)",
  },
  { value: "neither", label: "Neither – I'll pick folders myself" },
];

/** The media managers each connected source can be. A download client is not a media manager. */
export const MANAGER_KINDS_BY_SOURCE: Record<
  "deluno" | "arr",
  MediaManagerKind[]
> = {
  deluno: ["deluno"],
  arr: ["sonarr", "radarr"],
};

export function isConnectedSource(
  source: DownloadSource | null,
): source is ConnectedSource {
  return source !== null && source !== "neither";
}

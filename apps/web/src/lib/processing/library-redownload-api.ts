/**
 * "Download again" (#509): the files a past clean left missing a track this library's rules now keep, and the
 * request that asks the file's media manager to delete it and fetch the title again.
 */
import { apiFetch, readJson, requireOk } from "../api/client";
import { sendJson } from "../api/send-json";
import type { Schema } from "../api/types";

export type LibraryRedownloadTitle = Schema<"LibraryRedownloadTitleOut">;
export type LibraryRedownloads = Schema<"LibraryRedownloadsListOut">;
export type LibraryRedownloadResult = Schema<"LibraryRedownloadOut">;

function redownloadsPath(libraryId: number): string {
  return `/api/v1/processing/libraries/${libraryId}/library-redownloads`;
}

export async function fetchLibraryRedownloads(
  libraryId: number,
): Promise<LibraryRedownloads> {
  const path = redownloadsPath(libraryId);
  const r = await apiFetch(path);
  await requireOk(path, r, "Could not load the files missing tracks");
  return readJson<LibraryRedownloads>(r);
}

/** Only ever sent after the operator has confirmed: the media manager deletes the file before it searches. */
export async function requestLibraryRedownload(
  libraryId: number,
  filePath: string,
): Promise<LibraryRedownloadResult> {
  const r = await sendJson(
    redownloadsPath(libraryId),
    "POST",
    { path: filePath, confirm_destructive: true },
    "Could not ask your media manager to download this again",
  );
  return readJson<LibraryRedownloadResult>(r);
}

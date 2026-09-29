import { apiFetch, readJson, requireOk } from "../api/client";
import { sendJson } from "../api/send-json";
import type { Schema } from "../api/types";
import type { ProcessingMediaType } from "./libraries-api";

/** A library first-run setup offers to create or fill in, from what a connected manager or download client reports. */
export type SuggestedLibrary = Schema<"SuggestedLibraryOut">;
export type LibrarySuggestions = Schema<"LibrarySuggestionsOut">;
export type ProposedLibraryCheck = Schema<"ProposedLibraryCheckItemOut">;

export type ProposedFolders = { watched: string; output: string };

/** The folders to check, by media type; a type left out is not checked. */
export type ProposedFoldersByType = Partial<
  Record<ProcessingMediaType, ProposedFolders>
>;

export type ProposedLibraryChecks = Partial<
  Record<ProcessingMediaType, ProposedLibraryCheck>
>;

const SUGGESTIONS_PATH = "/api/v1/processing/library-suggestions";
const CHECK_PATH = "/api/v1/processing/library-check";

export async function fetchLibrarySuggestions(): Promise<LibrarySuggestions> {
  const response = await apiFetch(SUGGESTIONS_PATH);
  await requireOk(
    SUGGESTIONS_PATH,
    response,
    "Could not ask your connections for their folders",
  );
  return readJson<LibrarySuggestions>(response);
}

/** What creating libraries with these folders would run into, without creating anything. */
export async function checkProposedLibraries(
  folders: ProposedFoldersByType,
): Promise<ProposedLibraryChecks> {
  const body: Record<string, string> = {};
  for (const [mediaType, pair] of Object.entries(folders)) {
    body[`${mediaType}_watched_folder`] = pair.watched;
    body[`${mediaType}_output_folder`] = pair.output;
  }
  const response = await sendJson(
    CHECK_PATH,
    "POST",
    body,
    "Could not check those folders",
  );
  const result = await readJson<Schema<"ProposedLibraryCheckOut">>(response);
  return {
    ...(result.movie ? { movie: result.movie } : {}),
    ...(result.tv ? { tv: result.tv } : {}),
  };
}

import { apiFetch, readJson, requireOk } from "../api/client";
import type { Schema } from "../api/types";
import { withQuery } from "./files-api";

/** What the newest library clean did to one file (#695): History lists these beside the downloads. */
export type LibraryClean = Schema<"ProcessingLibraryCleanOut">;
export type LibraryCleansPage = Schema<"ProcessingLibraryCleansOut">;

export interface LibraryCleansQuery {
  library_id?: number;
  path_contains?: string;
  within_days?: number;
  limit?: number;
}

export async function fetchLibraryCleans(
  query: LibraryCleansQuery,
): Promise<LibraryCleansPage> {
  const path = withQuery("/api/v1/processing/library-cleans", query);
  const response = await apiFetch(path);
  await requireOk(path, response, "Could not load library cleans");
  return readJson<LibraryCleansPage>(response);
}

import { useQuery } from "@tanstack/react-query";

import {
  checkProposedLibraries,
  fetchLibrarySuggestions,
  type ProposedFoldersByType,
} from "./library-setup-api";
import { processingKeys } from "./query-keys";

/**
 * The libraries first-run setup can offer from what is connected. Keyed on which connections are answering,
 * so it is asked again when one is added or removed, and only once at least one answers: each ask reads from
 * the managers.
 */
export function useLibrarySuggestionsQuery(answeringConnections: string[]) {
  return useQuery({
    queryKey: processingKeys.librarySuggestions(answeringConnections),
    queryFn: fetchLibrarySuggestions,
    enabled: answeringConnections.length > 0,
    refetchOnWindowFocus: false,
    retry: false,
  });
}

/** What creating libraries with these folders would run into, checked again once the folders settle. */
export function useProposedLibraryCheckQuery(
  folders: ProposedFoldersByType,
  enabled: boolean,
) {
  return useQuery({
    queryKey: processingKeys.proposedLibraryCheck(folders),
    queryFn: () => checkProposedLibraries(folders),
    enabled,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
    retry: false,
  });
}

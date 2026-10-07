import { useMediaManagerConnectionsQuery } from "../media-managers/queries";
import { useProcessingLibrariesQuery } from "./libraries-queries";
import { handedOffNote, workflowKindOf } from "./workflow-kind";

/**
 * Why Weir does not scan one workflow ("Deluno hands this workflow its downloads."), or null when its own scan feeds it.
 * Asking for a scan of a workflow with a note is refused by the server, so nothing offers one.
 */
export function useHandedOffNote(libraryId: number): string | null {
  const libraries = useProcessingLibrariesQuery();
  const connections = useMediaManagerConnectionsQuery();
  const library = libraries.data?.find((item) => item.id === libraryId);
  return library
    ? handedOffNote(workflowKindOf(library, connections.data ?? []))
    : null;
}

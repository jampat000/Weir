import { useMemo, useState } from "react";

import type { ProcessingMediaType } from "../../lib/processing/libraries-api";
import type {
  ProposedFoldersByType,
  ProposedLibraryCheck,
} from "../../lib/processing/library-setup-api";
import {
  useLibrarySuggestionsQuery,
  useProposedLibraryCheckQuery,
} from "../../lib/processing/library-setup-queries";
import { useDebouncedValue } from "../../lib/ui/use-debounced-value";

/** The folders as they are typed settle for a moment before each check. */
const SETTLE_MS = 700;

/** One library setup offers, with whatever the person has changed and what the check says about it. */
export type SuggestedRow = {
  mediaType: ProcessingMediaType;
  /** The empty library this fills in, or null when it is a new one. */
  libraryId: number | null;
  name: string;
  sourceLabel: string;
  managerConnectionIds: number[];
  checked: boolean;
  watched: string;
  output: string;
  /** Null until the folders have been checked, and while they are being edited. */
  check: ProposedLibraryCheck | null;
};

/** What Finish saves for a ticked row. */
export type SuggestedLibraryPlan = Pick<
  SuggestedRow,
  "libraryId" | "name" | "mediaType" | "managerConnectionIds"
> & { watched: string; output: string };

type RowEdit = Partial<Pick<SuggestedRow, "checked" | "watched" | "output">>;
type Edits = Partial<Record<ProcessingMediaType, RowEdit>>;

function foldersOf(rows: SuggestedRow[]): ProposedFoldersByType {
  const folders: ProposedFoldersByType = {};
  for (const row of rows) {
    if (row.checked && (row.watched.trim() || row.output.trim())) {
      folders[row.mediaType] = {
        watched: row.watched.trim(),
        output: row.output.trim(),
      };
    }
  }
  return folders;
}

/** Why a ticked row cannot be saved as it stands, or null when it can. */
function missingFolder(row: SuggestedRow): string | null {
  if (!row.watched.trim()) {
    return `Add a watched folder for ${row.name}, or untick it.`;
  }
  if (!row.output.trim()) {
    return `Add an output folder for ${row.name}: cleaned files need somewhere to go.`;
  }
  return null;
}

/**
 * The libraries offered from the connections that answer, editable and checked before anything is created.
 * Each row starts from what the server suggests; the person's changes are kept apart from it, so asking again
 * after another connection is made never throws them away.
 */
export function useSuggestedLibraries(answeringConnections: string[]) {
  const suggestions = useLibrarySuggestionsQuery(answeringConnections);
  const [edits, setEdits] = useState<Edits>({});

  const offered = useMemo(
    () =>
      (suggestions.data?.libraries ?? []).map((library): SuggestedRow => ({
        mediaType: library.media_type,
        libraryId: library.library_id,
        name: library.name,
        sourceLabel: library.source_label,
        managerConnectionIds: library.manager_connection_ids,
        checked: edits[library.media_type]?.checked ?? true,
        watched: edits[library.media_type]?.watched ?? library.watched_folder,
        output: edits[library.media_type]?.output ?? library.output_folder,
        check: null,
      })),
    [suggestions.data, edits],
  );

  const proposed = useMemo(() => foldersOf(offered), [offered]);
  const settled = useDebouncedValue(proposed, SETTLE_MS);
  const checks = useProposedLibraryCheckQuery(
    settled,
    Object.keys(settled).length > 0,
  );
  const checksCurrent = settled === proposed && checks.data !== undefined;

  const rows = offered.map((row) => ({
    ...row,
    check: checksCurrent ? (checks.data?.[row.mediaType] ?? null) : null,
  }));
  const ticked = rows.filter((row) => row.checked);

  const blockedReason =
    ticked.map(missingFolder).find((reason) => reason !== null) ??
    ticked.find((row) => row.check?.problem)?.check?.problem ??
    null;

  return {
    status: suggestions,
    rows,
    notes: suggestions.data?.notes ?? [],
    checking: checks.isFetching || settled !== proposed,
    checkFailed: checks.isError,
    blockedReason,
    plan: ticked.map((row): SuggestedLibraryPlan => ({
      libraryId: row.libraryId,
      name: row.name,
      mediaType: row.mediaType,
      managerConnectionIds: row.managerConnectionIds,
      watched: row.watched.trim(),
      output: row.output.trim(),
    })),
    edit: (mediaType: ProcessingMediaType, change: RowEdit) =>
      setEdits((current) => ({
        ...current,
        [mediaType]: { ...current[mediaType], ...change },
      })),
  };
}

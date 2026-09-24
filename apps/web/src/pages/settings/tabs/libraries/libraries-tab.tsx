import { useState } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import { errorMessage } from "../../../../lib/api/error-message";
import { useCanEdit } from "../../../../lib/auth/can-edit";
import { useMediaManagerConnectionsQuery } from "../../../../lib/media-managers/queries";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import {
  useCreateProcessingLibrary,
  useDeleteProcessingLibrary,
  useProcessingLibrariesQuery,
  useProcessingRuleSetsQuery,
  useReorderProcessingLibraries,
  useUnlinkDiscoveredProcessingLibrary,
  useUpdateProcessingLibrary,
} from "../../../../lib/processing/libraries-queries";
import { SaveModelNote } from "../../save-model-note";
import { SettingsLoadError } from "../../settings-load-error";
import { LibraryEditor } from "./library-editor";
import {
  EMPTY_LIBRARY_FORM,
  formFrom,
  writeFrom,
  type LibraryForm,
} from "./library-form";
import { LibraryImportSection } from "./library-import-section";
import { LibraryListSection } from "./library-list-section";
import { RemoveLibraryDialog } from "./remove-library-dialog";

type Editing =
  | { kind: "closed" }
  | { kind: "adding" }
  | { kind: "editing"; library: ProcessingLibrary };

/**
 * Settings › Libraries: add, edit, reorder, switch on and off, and remove the libraries Weir
 * watches. A library is a row, so a fourth one is ordinary rather than a schema change (ADR-0014).
 * The list's own switches and arrows save at once; the editor saves only on its Save.
 */
export function LibrariesTab() {
  const editable = useCanEdit();
  const libraries = useProcessingLibrariesQuery();
  const ruleSets = useProcessingRuleSetsQuery();
  const connections = useMediaManagerConnectionsQuery();
  const create = useCreateProcessingLibrary();
  const update = useUpdateProcessingLibrary();
  const remove = useDeleteProcessingLibrary();
  const reorder = useReorderProcessingLibraries();
  const unlinkDiscovered = useUnlinkDiscoveredProcessingLibrary();

  const [editing, setEditing] = useState<Editing>({ kind: "closed" });
  const [removing, setRemoving] = useState<ProcessingLibrary | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  if (libraries.isPending) return <PageLoading label="Loading libraries" />;
  if (libraries.isError) return <SettingsLoadError what="libraries" />;

  const rows = [...libraries.data].sort(
    (a, b) => a.display_order - b.display_order,
  );
  const managers = connections.data ?? [];

  /** Runs a change, clearing the last notice first and saying why if it fails. */
  const attempt = (work: Promise<unknown>, failure: string) => {
    setNotice(null);
    work.catch((error: unknown) => setNotice(errorMessage(error, failure)));
  };

  const saveLibrary = (form: LibraryForm) =>
    editing.kind === "editing"
      ? update.mutateAsync({
          id: editing.library.id,
          data: writeFrom(form, editing.library),
        })
      : create.mutateAsync(writeFrom(form));

  const move = (library: ProcessingLibrary, direction: -1 | 1) => {
    const index = rows.findIndex((r) => r.id === library.id);
    const target = index + direction;
    if (index < 0 || target < 0 || target >= rows.length) return;
    const swapped = [...rows];
    [swapped[index], swapped[target]] = [swapped[target], swapped[index]];
    attempt(
      reorder.mutateAsync(swapped.map((r) => r.id)),
      "Libraries could not be reordered.",
    );
  };

  return (
    <div className="mm-quiet-stack" data-testid="processing-libraries-section">
      <SaveModelNote model="instant" />
      {notice ? (
        <p
          className="text-sm font-medium text-mm-text1"
          role="status"
          data-testid="processing-library-notice"
        >
          {notice}
        </p>
      ) : null}

      <LibraryListSection
        libraries={rows}
        ruleSets={ruleSets.data ?? []}
        connections={managers}
        editable={editable}
        onAdd={() => {
          setNotice(null);
          setEditing({ kind: "adding" });
        }}
        actions={{
          onToggle: (library) =>
            attempt(
              update.mutateAsync({
                id: library.id,
                data: {
                  ...writeFrom(formFrom(library), library),
                  enabled: !library.enabled,
                },
              }),
              "That library could not be changed.",
            ),
          onMove: move,
          onEdit: (library) => {
            setNotice(null);
            setEditing({ kind: "editing", library });
          },
          onUnlink: (library) =>
            attempt(
              unlinkDiscovered
                .mutateAsync(library.id)
                .then(() =>
                  setNotice(
                    `${library.name} is now a manual library. Its folders and settings were not changed.`,
                  ),
                ),
              "That library could not be unlinked.",
            ),
          onRemove: (library) => {
            remove.reset();
            setRemoving(library);
          },
          unlinking: unlinkDiscovered.isPending,
        }}
      />

      {editable && managers.length > 0 ? (
        <LibraryImportSection connections={managers} onNotice={setNotice} />
      ) : null}

      {editing.kind !== "closed" ? (
        <LibraryEditor
          key={editing.kind === "editing" ? editing.library.id : "new"}
          library={editing.kind === "editing" ? editing.library : undefined}
          initial={
            editing.kind === "editing"
              ? formFrom(editing.library)
              : EMPTY_LIBRARY_FORM
          }
          editable={editable}
          ruleSets={ruleSets.data ?? []}
          connections={managers}
          onSave={saveLibrary}
          onClose={() => setEditing({ kind: "closed" })}
        />
      ) : null}

      {removing ? (
        <RemoveLibraryDialog
          library={removing}
          remove={remove}
          onClose={() => setRemoving(null)}
        />
      ) : null}
    </div>
  );
}

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
import { LibraryEditor } from "./library-editor";
import {
  EMPTY_LIBRARY_FORM,
  formFrom,
  writeFrom,
  type LibraryForm,
} from "./library-form";
import { LibraryImportSection } from "./library-import-section";
import { LibraryListSection } from "./library-list-section";

type Editing =
  { kind: "closed" } | { kind: "adding" } | { kind: "editing"; id: number };

/**
 * Settings › Libraries: add, edit, reorder, switch on and off, and remove the libraries Weir
 * watches. A library is a row, so a fourth one is ordinary rather than a schema change (ADR-0014).
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
  const [form, setForm] = useState<LibraryForm>(EMPTY_LIBRARY_FORM);
  const [notice, setNotice] = useState<string | null>(null);

  if (libraries.isLoading) return <PageLoading label="Loading libraries" />;

  const rows = [...(libraries.data ?? [])].sort(
    (a, b) => a.display_order - b.display_order,
  );
  const managers = connections.data ?? [];
  const editingLibrary =
    editing.kind === "editing"
      ? rows.find((r) => r.id === editing.id)
      : undefined;

  /** Runs a change, clearing the last notice first and saying why if it fails. */
  const attempt = (work: Promise<unknown>, failure: string) => {
    setNotice(null);
    work.catch((error: unknown) => setNotice(errorMessage(error, failure)));
  };

  const open = (next: Editing, values: LibraryForm) => {
    setForm(values);
    setEditing(next);
    setNotice(null);
  };
  const close = () => {
    setEditing({ kind: "closed" });
    setNotice(null);
  };

  const save = () =>
    attempt(
      (editing.kind === "editing"
        ? update.mutateAsync({
            id: editing.id,
            data: writeFrom(form, editingLibrary),
          })
        : create.mutateAsync(writeFrom(form))
      ).then(close),
      "That library could not be saved.",
    );

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
        onAdd={() => open({ kind: "adding" }, EMPTY_LIBRARY_FORM)}
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
          onEdit: (library) =>
            open({ kind: "editing", id: library.id }, formFrom(library)),
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
          // The refusal reason is the useful part: it says how much work is in flight.
          onRemove: (library) =>
            attempt(
              remove.mutateAsync(library.id),
              "That library could not be removed.",
            ),
          unlinking: unlinkDiscovered.isPending,
        }}
      />

      {editable && managers.length > 0 ? (
        <LibraryImportSection connections={managers} onNotice={setNotice} />
      ) : null}

      <LibraryEditor
        open={editing.kind !== "closed"}
        library={editingLibrary}
        binding={{
          form,
          update: (patch) => setForm((current) => ({ ...current, ...patch })),
          editable,
        }}
        ruleSets={ruleSets.data ?? []}
        connections={managers}
        onSave={save}
        onClose={close}
      />
    </div>
  );
}

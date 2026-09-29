import { useEffect } from "react";
import { useSearchParams } from "react-router-dom";

import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";

const ADD_FROM_PARAM = "addFrom";
const EDIT_PARAM = "edit";

export type WorkflowDeepLink =
  | { kind: "add-from"; connectionId: number }
  | { kind: "edit"; library: ProcessingLibrary };

function positiveId(raw: string | null): number | null {
  const id = Number(raw);
  return raw !== null && Number.isInteger(id) && id > 0 ? id : null;
}

/**
 * Pages elsewhere link into Workflows: a media manager's "Add a workflow from ..." opens the add choice already
 * on that manager, and "See its folder chain" opens that workflow's editor. The link is used once and removed
 * from the address, so a reload does not reopen it.
 */
export function useWorkflowDeepLinks({
  libraries,
  connectionsLoaded,
  onOpen,
}: {
  /** Undefined until they have loaded. */
  libraries: ProcessingLibrary[] | undefined;
  connectionsLoaded: boolean;
  onOpen: (link: WorkflowDeepLink) => void;
}) {
  const [params, setParams] = useSearchParams();
  const addFrom = positiveId(params.get(ADD_FROM_PARAM));
  const edit = positiveId(params.get(EDIT_PARAM));
  const editing = libraries?.find((library) => library.id === edit);

  useEffect(() => {
    const link: WorkflowDeepLink | null =
      addFrom !== null && connectionsLoaded
        ? { kind: "add-from", connectionId: addFrom }
        : editing
          ? { kind: "edit", library: editing }
          : null;
    if (!link) return;
    onOpen(link);
    setParams(
      (current) => {
        const next = new URLSearchParams(current);
        next.delete(ADD_FROM_PARAM);
        next.delete(EDIT_PARAM);
        return next;
      },
      { replace: true },
    );
  }, [addFrom, connectionsLoaded, editing, onOpen, setParams]);
}

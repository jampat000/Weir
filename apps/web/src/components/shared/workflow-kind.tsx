import { Chip } from "../panels/chip";
import {
  workflowBadgeLabel,
  workflowKindNote,
  type WorkflowKind,
} from "../../lib/processing/workflow-kind";

/** The kind of a workflow as a badge; linked ones read as connected, Weir-only ones as plain. */
export function WorkflowKindBadge({ kind }: { kind: WorkflowKind }) {
  return (
    <Chip
      tone={kind.kind === "linked" ? "info" : "neutral"}
      dot={false}
      data-testid="workflow-kind-badge"
    >
      {workflowBadgeLabel(kind)}
    </Chip>
  );
}

/** The badge and its one-line explanation, one under the other. */
export function WorkflowKindSummary({ kind }: { kind: WorkflowKind }) {
  return (
    <>
      <span className="mm-library-source">
        <WorkflowKindBadge kind={kind} />
      </span>
      <span className="mm-quiet-table__sub">{workflowKindNote(kind)}</span>
    </>
  );
}

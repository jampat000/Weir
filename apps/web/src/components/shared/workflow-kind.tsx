import {
  workflowBadgeLabel,
  workflowKindNote,
  type WorkflowKind,
} from "../../lib/processing/workflow-kind";

/** The kind of a workflow as a badge; linked ones read as connected, Weir-only ones as plain. */
export function WorkflowKindBadge({ kind }: { kind: WorkflowKind }) {
  return (
    <span
      className={`mm-quiet-badge${kind.kind === "linked" ? " mm-workflow-badge--linked" : ""}`}
      data-testid="workflow-kind-badge"
    >
      {workflowBadgeLabel(kind)}
    </span>
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

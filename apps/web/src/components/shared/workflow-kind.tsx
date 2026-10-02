import { Chip } from "../panels/chip";
import {
  workflowBadgeLabel,
  workflowKindNote,
  type WorkflowKind,
} from "../../lib/processing/workflow-kind";

/** The kind of a workflow as a plain badge: what it is, not how it is doing. */
export function WorkflowKindBadge({
  kind,
  className,
}: {
  kind: WorkflowKind;
  className?: string;
}) {
  const label = workflowBadgeLabel(kind);
  return (
    <Chip
      dot={false}
      className={className}
      title={label}
      data-testid="workflow-kind-badge"
    >
      {label}
    </Chip>
  );
}

/** The badge and its one-line explanation, one under the other. */
export function WorkflowKindSummary({ kind }: { kind: WorkflowKind }) {
  return (
    <>
      <span className="block">
        <WorkflowKindBadge kind={kind} />
      </span>
      <span className="mm-quiet-table__sub">{workflowKindNote(kind)}</span>
    </>
  );
}

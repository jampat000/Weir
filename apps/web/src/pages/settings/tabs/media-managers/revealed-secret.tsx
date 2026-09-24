import { mmTechnicalMonoSmallClass } from "../../../../lib/ui/mm-control-roles";

/** A webhook secret shown exactly once: Weir does not keep it around to show again. */
export function RevealedSecret({
  managerName,
  secret,
}: {
  managerName: string;
  secret: string;
}) {
  return (
    <div
      className="mt-2 rounded bg-mm-card-bg p-2"
      data-testid="media-manager-secret"
    >
      <code className={mmTechnicalMonoSmallClass}>{secret}</code>
      <span className="mt-1 block text-mm-text3">
        Copy this into {managerName} now — Weir will not show it again.
      </span>
    </div>
  );
}

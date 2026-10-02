import type { SystemSettingsForm } from "../../use-system-settings-form";
import { RetentionSection } from "./retention-section";
import { ServerDiagnostics } from "./server-diagnostics";
import { UnifiedLog } from "./unified-log";

/**
 * System › Logs: one log of everything Weir recorded, its server's counters folded below it, and how long each kind of
 * record is kept. A file's story is on Activity, not here.
 */
export function LogsTab({
  form,
  editable,
  savedLogDays,
}: {
  form: SystemSettingsForm;
  editable: boolean;
  savedLogDays: number;
}) {
  return (
    <div className="mm-sys-stack" data-testid="settings-logs">
      <UnifiedLog />
      <ServerDiagnostics />
      <RetentionSection
        form={form}
        editable={editable}
        savedLogDays={savedLogDays}
      />
    </div>
  );
}

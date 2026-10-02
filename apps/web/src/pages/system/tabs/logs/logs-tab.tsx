import { useSearchParams } from "react-router-dom";

import type { SystemSettingsForm } from "../../use-system-settings-form";
import { ActivityLog } from "./activity-log";
import { JobsSection } from "./jobs-section";
import { LogsHeader, logViewFrom, type LogView } from "./logs-header-controls";
import { RetentionSection } from "./retention-section";
import { ServerLog } from "./server-log";

/**
 * System › Logs: Weir's own events, its jobs or its server log, with how long they are kept beside them. The choice
 * and the open list's filters are on the header's title line. A file's story is on History, not here.
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
  const [searchParams, setSearchParams] = useSearchParams();
  const view = logViewFrom(searchParams.get("show"));

  function showView(next: LogView): void {
    const nextParams = new URLSearchParams(searchParams);
    nextParams.set("tab", "logs");
    if (next === "activity") nextParams.delete("show");
    else nextParams.set("show", next);
    for (const name of ["status", "path"]) nextParams.delete(name);
    setSearchParams(nextParams);
  }

  return (
    <div className="mm-sys-stack" data-testid="settings-history">
      <LogsHeader view={view} onView={showView}>
        {view === "activity" ? (
          <ActivityLog />
        ) : view === "jobs" ? (
          <JobsSection />
        ) : (
          <ServerLog />
        )}
      </LogsHeader>
      <RetentionSection
        form={form}
        editable={editable}
        savedLogDays={savedLogDays}
      />
    </div>
  );
}

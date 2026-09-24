import { useSearchParams } from "react-router-dom";

import type { SystemSettingsForm } from "../../use-system-settings-form";
import { ActivityLog } from "./activity-log";
import { HistoryResetSection } from "./history-reset-section";
import { JobsSection } from "./jobs-section";
import { RetentionSection } from "./retention-section";
import { ServerLog } from "./server-log";

/** What Logs shows: one "Show" choice beside the list, never a second row of tabs. */
type LogView = "activity" | "log" | "jobs";

const LOG_VIEWS: { id: LogView; label: string }[] = [
  { id: "activity", label: "Events" },
  { id: "jobs", label: "Weir's jobs" },
  { id: "log", label: "Server log" },
];

/** The address's `show`, or Events when it names nothing Logs has. */
function logViewFrom(candidate: string | null): LogView {
  return LOG_VIEWS.find((view) => view.id === candidate)?.id ?? "activity";
}

/**
 * System › Logs: Weir's own events, its jobs or its server log, with how long they are kept and how
 * to clear them beside them. A file's story is on History, not here.
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
    <div className="mm-quiet-stack" data-testid="settings-history">
      <label className="mm-history-show">
        <span>Show</span>
        <select
          className="mm-input"
          data-testid="settings-history-show"
          value={view}
          onChange={(e) => showView(logViewFrom(e.target.value))}
        >
          {LOG_VIEWS.map((option) => (
            <option key={option.id} value={option.id}>
              {option.label}
            </option>
          ))}
        </select>
      </label>
      {view === "activity" ? (
        <ActivityLog />
      ) : view === "jobs" ? (
        <JobsSection />
      ) : (
        <ServerLog />
      )}
      <div data-testid="suite-settings-retention" className="mm-quiet-stack">
        <RetentionSection
          form={form}
          editable={editable}
          savedLogDays={savedLogDays}
        />
        {editable ? <HistoryResetSection /> : null}
      </div>
    </div>
  );
}

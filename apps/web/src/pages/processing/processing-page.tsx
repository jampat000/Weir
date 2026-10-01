/**
 * The Dashboard, Weir's landing page, in two views kept in the address: Live, everything Weir is doing
 * right now, and System, how it is set up and what it does in the background. The header carries the
 * switch between them, on Live the kind of work to show, and the workflow to narrow everything to.
 */
import { useState } from "react";

import { PageHeader } from "../../components/shell/page-header";
import { ShellHeaderSlot } from "../../components/shell/shell-header-context";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { DashboardControls } from "./dashboard/dashboard-controls";
import { LiveView } from "./dashboard/live-view";
import { SystemView } from "./dashboard/system-view";
import { useDashboardAddress } from "./dashboard-address";
import type { Filter } from "./processing-filter";
import { LIBRARIES_REFRESH_MS } from "./use-processing-lanes";

const EYEBROW = "Cleans new downloads and your library";

export function ProcessingPage(): React.ReactElement {
  const libraries = useProcessingLibrariesQuery(true, LIBRARIES_REFRESH_MS);
  const enabledWorkflows = libraries.data?.filter(
    (workflow) => workflow.enabled,
  );
  const address = useDashboardAddress(enabledWorkflows);
  const [filter, setFilter] = useState<Filter>("all");

  return (
    <div className="mm-page mm-dash" data-testid="processing-page">
      <PageHeader eyebrow={EYEBROW} />
      <ShellHeaderSlot>
        <DashboardControls
          address={address}
          filter={filter}
          onFilter={setFilter}
          workflows={enabledWorkflows ?? []}
        />
      </ShellHeaderSlot>
      {address.view === "system" ? (
        <SystemView workflowId={address.workflowId} />
      ) : (
        <LiveView filter={filter} workflowId={address.workflowId} />
      )}
    </div>
  );
}

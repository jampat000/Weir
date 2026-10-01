/**
 * The Dashboard, Weir's landing page, in two views kept in the address: Live, everything Weir is doing
 * right now, and System, how it is set up and what it does in the background. The header carries the
 * switch between them, on Live the kind of work to show, and the workflow to narrow everything to.
 *
 * The page decides its layout from the width of its own main area, not the window's. On Live, wide, it is
 * exactly as tall as the window, so everything is sized from the space it has and the page itself never
 * scrolls; narrower, or on a phone, it flows and scrolls like any page.
 */
import { useState } from "react";

import { PageHeader } from "../../components/shell/page-header";
import { ShellHeaderSlot } from "../../components/shell/shell-header-context";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { useElementSize } from "../../lib/ui/use-element-size";
import { useFitToScreen } from "../../lib/ui/use-fit-to-screen";
import { useRemPx } from "../../lib/ui/use-rem-px";
import { DashboardControls } from "./dashboard/dashboard-controls";
import { MIN_GRID_PX, pageLayout } from "./dashboard/dashboard-layout";
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
  const [pageRef, page] = useElementSize<HTMLDivElement>();
  const rem = useRemPx();
  const layout = pageLayout(page.width, rem);
  const live = address.view === "live";
  useFitToScreen(pageRef, live && layout.sideBySide, MIN_GRID_PX);

  return (
    <div
      ref={pageRef}
      className="mm-page mm-dash"
      data-testid="processing-page"
    >
      <PageHeader eyebrow={EYEBROW} />
      <ShellHeaderSlot>
        <DashboardControls
          address={address}
          filter={filter}
          onFilter={setFilter}
          workflows={enabledWorkflows ?? []}
        />
      </ShellHeaderSlot>
      {live ? (
        <LiveView
          filter={filter}
          workflowId={address.workflowId}
          layout={layout}
        />
      ) : (
        <SystemView workflowId={address.workflowId} layout={layout} />
      )}
    </div>
  );
}

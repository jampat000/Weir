/**
 * The Dashboard, Weir's landing page, in two views kept in the address: Live, everything Weir is doing
 * right now, and System, how it is set up and what it does in the background. The header carries the
 * tabs between them after the title and, on Live, the workflow to narrow everything to and the kind of work to show.
 *
 * The page decides its layout from the width of its own main area, not the window's. On Live, wide, it is
 * exactly as tall as the window, so everything is sized from the space it has and the page itself never
 * scrolls; narrower, or on a phone, it flows and scrolls like any page.
 */
import { PageHeader } from "../../components/shell/page-header";
import { ShellHeaderSlot } from "../../components/shell/shell-header-context";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { useElementSize } from "../../lib/ui/use-element-size";
import { useFitToScreen } from "../../lib/ui/use-fit-to-screen";
import { useRemPx } from "../../lib/ui/use-rem-px";
import {
  DashboardControls,
  DashboardTabs,
} from "./dashboard/dashboard-controls";
import { MIN_GRID_PX, pageLayout } from "./dashboard/dashboard-layout";
import { LiveView } from "./dashboard/live-view";
import { SystemView } from "./dashboard/system-view";
import { useDashboardAddress } from "./dashboard-address";

const EYEBROW = "Cleans new downloads and your library";

export function ProcessingPage(): React.ReactElement {
  const libraries = useProcessingLibrariesQuery();
  const enabledWorkflows = libraries.data?.filter(
    (workflow) => workflow.enabled,
  );
  const address = useDashboardAddress(enabledWorkflows);
  const [pageRef, page] = useElementSize<HTMLDivElement>();
  const rem = useRemPx();
  const layout = pageLayout(page.width, rem);
  const live = address.view === "live";
  useFitToScreen(pageRef, layout.sideBySide, MIN_GRID_PX);

  return (
    <div
      ref={pageRef}
      className="mm-page mm-dash"
      data-testid="processing-page"
    >
      <PageHeader eyebrow={EYEBROW} />
      <DashboardTabs address={address} />
      <ShellHeaderSlot>
        <DashboardControls
          address={address}
          workflows={enabledWorkflows ?? []}
        />
      </ShellHeaderSlot>
      {live ? (
        <LiveView
          filter={address.filter}
          workflowId={address.workflowId}
          layout={layout}
        />
      ) : (
        <SystemView layout={layout} />
      )}
    </div>
  );
}

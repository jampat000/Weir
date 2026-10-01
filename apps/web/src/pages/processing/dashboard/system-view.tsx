/**
 * The Dashboard's System view: how Weir is set up and what it does in the background. The health of every
 * workflow, connection and tool in full, then the jobs Weir has run and the timers it keeps. Each part loads
 * and fails on its own. Wide, its two columns are Live's, so nothing moves between the views; it lists every
 * workflow, so it scrolls where Live is held to the window.
 */
import type { CSSProperties } from "react";

import { ApiEntryError } from "../../../components/shared/api-entry-error";
import { PageLoading } from "../../../components/shared/page-loading";
import { usePauseQuery } from "../../../lib/pause/pause-queries";
import { useProcessingLibrariesQuery } from "../../../lib/processing/libraries-queries";
import { classNames } from "../../../lib/ui/class-names";
import { useNow } from "../../../lib/ui/use-now";
import { JobsSection } from "../../system/tabs/logs/jobs-section";
import { LIBRARIES_REFRESH_MS } from "../use-processing-lanes";
import { GRID_COLUMNS, type PageLayout } from "./dashboard-layout";
import { HealthDetail } from "./health-detail";
import { TimersPanel } from "./timers-panel";
import { useNextItems } from "./use-next-items";

/** Once a second, so the timers' countdowns move between server updates. */
const TICK_MS = 1000;

type SystemViewProps = {
  /** Narrows the health and the timers to one workflow; every workflow when null. */
  workflowId: number | null;
  /** How the page lays itself out, decided from the width of its main area. */
  layout: PageLayout;
};

/** The columns the view's two rows share when the right column sits beside the page. */
const COLUMNS_STYLE = { "--dash-columns": GRID_COLUMNS } as CSSProperties;

export function SystemView({ workflowId, layout }: SystemViewProps) {
  const workflows = useProcessingLibrariesQuery(true, LIBRARIES_REFRESH_MS);
  const pause = usePauseQuery();
  const now = useNow(TICK_MS);
  const next = useNextItems(workflows.data, workflowId);

  if (workflows.isPending) return <PageLoading label="Loading the system" />;
  if (workflows.isError) return <ApiEntryError error={workflows.error} />;

  return (
    <div
      className={classNames("mm-sys", layout.sideBySide && "mm-sys--beside")}
      style={COLUMNS_STYLE}
      data-testid="dashboard-system"
    >
      <HealthDetail workflows={workflows.data} workflowId={workflowId} />
      <div className="mm-sys__low">
        <div className="mm-sys__jobs">
          <JobsSection />
        </div>
        <TimersPanel
          items={next}
          now={now}
          paused={pause.data?.paused ?? false}
        />
      </div>
    </div>
  );
}

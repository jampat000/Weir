import { SegmentedControl } from "../../../components/panels/segmented-control";
import {
  PageTabs,
  type PageTabOption,
} from "../../../components/shell/page-tabs";
import { ShellHeaderTabs } from "../../../components/shell/shell-header-context";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { DashboardAddress, DashboardView } from "../dashboard-address";
import { FILTER_OPTIONS } from "../processing-filter";
import { WorkPicker } from "./work-picker";
import { WorkflowPicker } from "./workflow-picker";

const VIEW_TABS: readonly PageTabOption<DashboardView>[] = [
  { id: "live", label: "Live" },
  { id: "system", label: "System" },
];

/** The Live and System views, as tabs on the header's title line, right after "Dashboard". */
export function DashboardTabs({ address }: { address: DashboardAddress }) {
  return (
    <ShellHeaderTabs>
      <PageTabs
        tabs={VIEW_TABS}
        activeId={address.view}
        onSelect={address.setView}
        ariaLabel="Dashboard view"
        idPrefix="dashboard-view"
        placement="title"
        dataTestId="dashboard-view"
      />
    </ShellHeaderTabs>
  );
}

type DashboardControlsProps = {
  address: DashboardAddress;
  /** The workflows that are switched on. */
  workflows: readonly Pick<ProcessingLibrary, "id" | "name">[];
};

/**
 * What the Dashboard puts in the header besides its tabs: the workflow to narrow everything to, and on Live
 * the kind of work to show. The one control that comes and goes is last, so none of the others moves when the
 * view changes. The kind of work is a three-button switch where the header has room for one, and a
 * picker where it does not; the stylesheet chooses by the room the control has, so the header stays on one line.
 */
export function DashboardControls({
  address,
  workflows,
}: DashboardControlsProps) {
  return (
    <div className="mm-dash-controls">
      <WorkflowPicker
        workflows={workflows}
        value={address.workflowId}
        onChange={address.setWorkflowId}
      />
      {address.view === "live" ? (
        <div className="mm-dash-controls__work">
          <div className="mm-dash-controls__switch">
            <SegmentedControl
              options={FILTER_OPTIONS}
              value={address.filter}
              onChange={address.setFilter}
              ariaLabel="Show work from"
              dataTestId="live-filter"
            />
          </div>
          <div className="mm-dash-controls__compact">
            <WorkPicker value={address.filter} onChange={address.setFilter} />
          </div>
        </div>
      ) : null}
    </div>
  );
}

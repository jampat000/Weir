import {
  SegmentedControl,
  type SegmentedOption,
} from "../../../components/panels/segmented-control";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { DashboardAddress, DashboardView } from "../dashboard-address";
import { FILTER_OPTIONS } from "../processing-filter";
import { WorkPicker } from "./work-picker";
import { WorkflowPicker } from "./workflow-picker";

const VIEW_OPTIONS: readonly SegmentedOption<DashboardView>[] = [
  { value: "live", label: "Live" },
  { value: "system", label: "System" },
];

type DashboardControlsProps = {
  address: DashboardAddress;
  /** The workflows that are switched on. */
  workflows: readonly Pick<ProcessingLibrary, "id" | "name">[];
};

/**
 * What the Dashboard puts in the header: the Live and System views, the workflow to narrow everything to, and
 * on Live the kind of work to show. The one control that comes and goes is last, so none of the others moves
 * when the view changes. The kind of work is a three-button switch where the header has room for one, and a
 * picker where it does not; the stylesheet chooses by the room the control has, so the header stays on one line.
 */
export function DashboardControls({
  address,
  workflows,
}: DashboardControlsProps) {
  return (
    <div className="mm-dash-controls">
      <SegmentedControl
        options={VIEW_OPTIONS}
        value={address.view}
        onChange={address.setView}
        ariaLabel="Dashboard view"
        dataTestId="dashboard-view"
      />
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

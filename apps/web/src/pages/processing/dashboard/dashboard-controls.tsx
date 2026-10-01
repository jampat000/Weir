import {
  SegmentedControl,
  type SegmentedOption,
} from "../../../components/panels/segmented-control";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { DashboardAddress, DashboardView } from "../dashboard-address";
import { FILTER_OPTIONS, type Filter } from "../processing-filter";
import { WorkflowPicker } from "./workflow-picker";

const VIEW_OPTIONS: readonly SegmentedOption<DashboardView>[] = [
  { value: "live", label: "Live" },
  { value: "system", label: "System" },
];

type DashboardControlsProps = {
  address: DashboardAddress;
  filter: Filter;
  onFilter: (filter: Filter) => void;
  /** The workflows that are switched on. */
  workflows: readonly Pick<ProcessingLibrary, "id" | "name">[];
};

/**
 * What the Dashboard puts in the header: the Live and System views, on Live the choice of which kind of
 * work to show, and the workflow to narrow everything to.
 */
export function DashboardControls({
  address,
  filter,
  onFilter,
  workflows,
}: DashboardControlsProps) {
  return (
    <>
      <SegmentedControl
        options={VIEW_OPTIONS}
        value={address.view}
        onChange={address.setView}
        ariaLabel="Dashboard view"
        dataTestId="dashboard-view"
      />
      {address.view === "live" ? (
        <SegmentedControl
          options={FILTER_OPTIONS}
          value={filter}
          onChange={onFilter}
          ariaLabel="Show work from"
          dataTestId="live-filter"
        />
      ) : null}
      <WorkflowPicker
        workflows={workflows}
        value={address.workflowId}
        onChange={address.setWorkflowId}
      />
    </>
  );
}

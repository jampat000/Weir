import {
  createContext,
  useContext,
  useId,
  useState,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";

import { ShellHeaderSlot } from "../../../../components/shell/shell-header-context";
import { MmListboxPicker } from "../../../../components/ui/mm-listbox-picker";
import { useMediaQuery } from "../../../../lib/ui/use-media-query";

/** What Logs shows: one "Show" choice in the header, never a second row of tabs. */
export type LogView = "activity" | "log" | "jobs";

const LOG_VIEWS: { value: LogView; label: string }[] = [
  { value: "activity", label: "Events" },
  { value: "jobs", label: "Weir's jobs" },
  { value: "log", label: "Server log" },
];

/** The address's `show`, or Events when it names nothing Logs has. */
export function logViewFrom(candidate: string | null): LogView {
  return (
    LOG_VIEWS.find((view) => view.value === candidate)?.value ?? "activity"
  );
}

/** Below this the header cannot hold the search in full beside five pickers, so the search is a magnifier. */
const SEARCH_IN_FULL = "(min-width: 1600px)";

type HeaderControls = {
  /** Where the open view's own controls go, after "Show". */
  host: HTMLElement | null;
  /** Whether the search is a magnifier, because the header is short of room. */
  searchCollapsed: boolean;
};

const HeaderControlsContext = createContext<HeaderControls>({
  host: null,
  searchCollapsed: false,
});

/**
 * Logs' controls on the header's title line, after the title and its tabs, as History's are: the "Show" choice first,
 * then the open view's own filters, which it puts here with `LogsViewControls`. Short of room the search is a
 * magnifier and the pickers narrow, so the line stays one line.
 */
export function LogsHeader({
  view,
  onView,
  children,
}: {
  view: LogView;
  onView: (next: LogView) => void;
  children: ReactNode;
}) {
  const viewLabelId = useId();
  const [host, setHost] = useState<HTMLElement | null>(null);
  const searchCollapsed = !useMediaQuery(SEARCH_IN_FULL);

  return (
    <HeaderControlsContext.Provider value={{ host, searchCollapsed }}>
      <ShellHeaderSlot>
        <div
          className="mm-history-controls mm-logs-controls"
          data-testid="logs-controls"
        >
          <div className="mm-workflow-picker">
            <span id={viewLabelId} className="sr-only">
              Show
            </span>
            <MmListboxPicker
              data-testid="settings-history-show"
              options={LOG_VIEWS}
              value={view}
              onChange={(next) => onView(logViewFrom(next))}
              ariaLabelledBy={viewLabelId}
            />
          </div>
          <div className="mm-logs-view-controls" ref={setHost} />
        </div>
      </ShellHeaderSlot>
      {children}
    </HeaderControlsContext.Provider>
  );
}

/** The open view's controls, placed in the header after "Show". Outside the shell they stay where they are written. */
export function LogsViewControls({ children }: { children: ReactNode }) {
  const { host } = useContext(HeaderControlsContext);
  return host ? createPortal(children, host) : null;
}

/** True where the header is short of room and the search should be a magnifier. */
export function useSearchCollapsed(): boolean {
  return useContext(HeaderControlsContext).searchCollapsed;
}

/**
 * One quiet picker among the header's controls. Its default words say what it filters ("All events", "Any result"),
 * so it carries no label of its own; `label` names it for a screen reader.
 */
export function LogsPicker({
  label,
  options,
  value,
  onChange,
  wide = false,
  testId,
}: {
  label: string;
  options: readonly { value: string; label: string }[];
  value: string;
  onChange: (next: string) => void;
  /** For a choice whose words are long. */
  wide?: boolean;
  testId?: string;
}) {
  const labelId = useId();
  return (
    <div
      className={
        wide ? "mm-workflow-picker mm-logs-picker--wide" : "mm-workflow-picker"
      }
    >
      <span id={labelId} className="sr-only">
        {label}
      </span>
      <MmListboxPicker
        data-testid={testId}
        options={options}
        value={value}
        onChange={onChange}
        ariaLabelledBy={labelId}
      />
    </div>
  );
}

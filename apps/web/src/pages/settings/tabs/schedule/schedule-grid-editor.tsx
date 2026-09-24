import { useRef, useState, type KeyboardEvent } from "react";

import {
  DAY_FULL_NAMES,
  DAY_NAMES,
  HOURS_PER_DAY,
  SLOTS_PER_WEEK,
  hourIsOn,
  withHour,
} from "./schedule-model";

/** Hour labels are drawn every six hours, so 24 narrow columns stay readable. */
const HOUR_LABEL_EVERY = 6;
const LAST_DAY = DAY_NAMES.length - 1;
const LAST_HOUR = HOURS_PER_DAY - 1;

type Cell = { day: number; hour: number };

function clamp(value: number, max: number): number {
  return Math.min(Math.max(value, 0), max);
}

/** Where an arrow, Home or End key moves the focus from `cell`, or null for any other key. */
function cellAfterKey(cell: Cell, key: string): Cell | null {
  switch (key) {
    case "ArrowRight":
      return { ...cell, hour: clamp(cell.hour + 1, LAST_HOUR) };
    case "ArrowLeft":
      return { ...cell, hour: clamp(cell.hour - 1, LAST_HOUR) };
    case "ArrowDown":
      return { ...cell, day: clamp(cell.day + 1, LAST_DAY) };
    case "ArrowUp":
      return { ...cell, day: clamp(cell.day - 1, LAST_DAY) };
    case "Home":
      return { ...cell, hour: 0 };
    case "End":
      return { ...cell, hour: LAST_HOUR };
    default:
      return null;
  }
}

function cellName(day: number, hour: number, on: boolean): string {
  return `${DAY_FULL_NAMES[day]} ${String(hour).padStart(2, "0")}:00, ${on ? "on" : "off"}`;
}

/**
 * One cell in the tab order at a time (a roving tab stop): Tab reaches the grid once, and the arrow
 * keys move within it, so the page after it is one Tab away rather than 168.
 */
function useRovingCell() {
  const [active, setActive] = useState<Cell>({ day: 0, hour: 0 });
  const cells = useRef(new Map<string, HTMLButtonElement>());
  const keyOf = (cell: Cell) => `${cell.day}-${cell.hour}`;
  return {
    isActive: (cell: Cell) =>
      active.day === cell.day && active.hour === cell.hour,
    setActive,
    register: (cell: Cell) => (element: HTMLButtonElement | null) => {
      if (element) cells.current.set(keyOf(cell), element);
      else cells.current.delete(keyOf(cell));
    },
    moveTo: (cell: Cell) => {
      setActive(cell);
      cells.current.get(keyOf(cell))?.focus();
    },
  };
}

export interface ScheduleGridEditorProps {
  value: string;
  onChange: (grid: string) => void;
  disabled?: boolean;
}

/**
 * A 7x24 schedule. The stored grid is quarter-hour resolution, but the editor works in whole hours:
 * 168 targets are usable with a mouse and 672 are not. An hour toggle writes all four of its quarters,
 * and a 15-minute boundary can still be set through the API. Drag across it with a pointer, or use the
 * arrow keys and Space or Enter (#692).
 */
export function ScheduleGridEditor({
  value,
  onChange,
  disabled = false,
}: ScheduleGridEditorProps) {
  const [painting, setPainting] = useState<boolean | null>(null);
  const roving = useRovingCell();

  const toggle = (day: number, hour: number, on: boolean) => {
    if (disabled) return;
    onChange(withHour(value, day, hour, on));
  };

  const onKeyDown = (event: KeyboardEvent, cell: Cell, on: boolean) => {
    if (event.key === " " || event.key === "Enter") {
      event.preventDefault();
      toggle(cell.day, cell.hour, !on);
      return;
    }
    const next = cellAfterKey(cell, event.key);
    if (next) {
      event.preventDefault();
      roving.moveTo(next);
    }
  };

  const unrestricted = value.length !== SLOTS_PER_WEEK;

  return (
    <div className="mm-schedule-grid" data-testid="schedule-grid">
      <p className="text-xs text-mm-text3">
        {unrestricted
          ? "No schedule set — this library runs at any time. Select hours to limit it."
          : "Selected hours are when Weir may start work. Work already running finishes."}{" "}
        Drag across the hours, or use the arrow keys and press Space to switch
        one on or off.
      </p>
      <div
        className="mm-schedule-grid-body"
        role="group"
        aria-label="Hours in the week"
        onPointerUp={() => setPainting(null)}
        onPointerLeave={() => setPainting(null)}
      >
        <div className="mm-schedule-grid-hours" aria-hidden="true">
          <span />
          {Array.from({ length: HOURS_PER_DAY }, (_, hour) => (
            <span key={hour}>{hour % HOUR_LABEL_EVERY === 0 ? hour : ""}</span>
          ))}
        </div>
        {DAY_NAMES.map((label, day) => (
          <div className="mm-schedule-grid-row" key={label}>
            <span className="mm-schedule-grid-day" aria-hidden="true">
              {label}
            </span>
            {Array.from({ length: HOURS_PER_DAY }, (_, hour) => {
              const cell = { day, hour };
              const on = hourIsOn(value, day, hour);
              return (
                <button
                  key={hour}
                  ref={roving.register(cell)}
                  type="button"
                  disabled={disabled}
                  tabIndex={roving.isActive(cell) ? 0 : -1}
                  className={
                    on
                      ? "mm-schedule-cell mm-schedule-cell-on"
                      : "mm-schedule-cell"
                  }
                  aria-pressed={on}
                  aria-label={cellName(day, hour, on)}
                  data-testid={`schedule-cell-${day}-${hour}`}
                  onFocus={() => roving.setActive(cell)}
                  onKeyDown={(event) => onKeyDown(event, cell, on)}
                  onPointerDown={() => {
                    setPainting(!on);
                    toggle(day, hour, !on);
                  }}
                  // Dragging across the grid is how anyone actually draws a window;
                  // clicking 40 cells one at a time is not a schedule editor.
                  onPointerEnter={() => {
                    if (painting !== null) toggle(day, hour, painting);
                  }}
                />
              );
            })}
          </div>
        ))}
      </div>
      <div className="mm-schedule-grid-actions">
        <button
          type="button"
          disabled={disabled}
          className="mm-theme-toggle"
          data-testid="schedule-clear"
          onClick={() => onChange("")}
        >
          Any time
        </button>
        <button
          type="button"
          disabled={disabled}
          className="mm-theme-toggle"
          data-testid="schedule-none"
          onClick={() => onChange("0".repeat(SLOTS_PER_WEEK))}
        >
          Clear all
        </button>
      </div>
    </div>
  );
}

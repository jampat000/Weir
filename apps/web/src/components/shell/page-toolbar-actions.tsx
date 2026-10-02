import { createContext, useContext, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";

import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";

/** A button in the 40px toolbar row is shorter than one in a panel, so it sits inside the row instead of filling it. */
const TOOLBAR_BUTTON_SIZE = "min-h-8! py-1.5!";

/**
 * The element in a toolbar row where the open tab's own buttons go. Undefined outside a page with a toolbar
 * (as in a tab's own tests), where the buttons stay where they are written; null until the row has mounted.
 */
const ActionsSlotContext = createContext<HTMLElement | null | undefined>(
  undefined,
);
const SetActionsSlotContext = createContext<
  ((slot: HTMLElement | null) => void) | null
>(null);

/** Wraps a page's toolbar and the tabs under it, so a tab can put its buttons in the toolbar's row. */
export function PageToolbarActionsProvider({
  children,
}: {
  children: ReactNode;
}) {
  const [slot, setSlot] = useState<HTMLElement | null>(null);
  return (
    <SetActionsSlotContext.Provider value={setSlot}>
      <ActionsSlotContext.Provider value={slot}>
        {children}
      </ActionsSlotContext.Provider>
    </SetActionsSlotContext.Provider>
  );
}

/** What goes in a toolbar's `actions`: the place the open tab's buttons appear. */
export function PageToolbarActionsSlot() {
  const setSlot = useContext(SetActionsSlotContext);
  if (!setSlot) {
    throw new Error(
      "The toolbar's action slot belongs inside PageToolbarActionsProvider.",
    );
  }
  return <div className="mm-page-toolbar__slot" ref={setSlot} />;
}

/** Puts a tab's own buttons at the right of the page's toolbar row, or where they are written when there is none. */
export function PageToolbarAction({ children }: { children: ReactNode }) {
  const slot = useContext(ActionsSlotContext);
  if (slot === undefined) return <>{children}</>;
  return slot ? createPortal(children, slot) : null;
}

/** The page's one "add" button, in the toolbar row: "+ Add workflow". */
export function PageToolbarAddButton({
  label,
  onClick,
  disabled = false,
  dataTestId,
}: {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  dataTestId?: string;
}) {
  return (
    <PageToolbarAction>
      <button
        type="button"
        className={`${mmActionButtonClass({ variant: "primary" })} ${TOOLBAR_BUTTON_SIZE}`}
        data-testid={dataTestId}
        disabled={disabled}
        onClick={onClick}
      >
        <span aria-hidden="true" className="mr-1.5">
          +
        </span>
        {label}
      </button>
    </PageToolbarAction>
  );
}

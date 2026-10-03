import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";

type HeaderSetters = {
  setEyebrow: (eyebrow: string | null) => void;
  setSlot: (slot: HTMLElement | null) => void;
  setTabsSlot: (slot: HTMLElement | null) => void;
  setButtonsSlot: (slot: HTMLElement | null) => void;
};

/**
 * What a page may say to the shell's header. The setters never change, so a page that sets its eyebrow
 * does not re-render when the header does; the eyebrow and the slot are read through contexts of their own.
 */
const SettersContext = createContext<HeaderSetters | null>(null);
const EyebrowContext = createContext<string | null>(null);
const SlotContext = createContext<HTMLElement | null>(null);
const TabsSlotContext = createContext<HTMLElement | null>(null);
const ButtonsSlotContext = createContext<HTMLElement | null>(null);

export function ShellHeaderProvider({ children }: { children: ReactNode }) {
  const [eyebrow, setEyebrow] = useState<string | null>(null);
  const [slot, setSlot] = useState<HTMLElement | null>(null);
  const [tabsSlot, setTabsSlot] = useState<HTMLElement | null>(null);
  const [buttonsSlot, setButtonsSlot] = useState<HTMLElement | null>(null);
  const setters = useMemo(
    () => ({ setEyebrow, setSlot, setTabsSlot, setButtonsSlot }),
    [],
  );
  return (
    <SettersContext.Provider value={setters}>
      <EyebrowContext.Provider value={eyebrow}>
        <SlotContext.Provider value={slot}>
          <TabsSlotContext.Provider value={tabsSlot}>
            <ButtonsSlotContext.Provider value={buttonsSlot}>
              {children}
            </ButtonsSlotContext.Provider>
          </TabsSlotContext.Provider>
        </SlotContext.Provider>
      </EyebrowContext.Provider>
    </SettersContext.Provider>
  );
}

/** The eyebrow a page has set for the header, or null when the page leaves it to the menu's own line. */
export function usePageEyebrow(): string | null {
  return useContext(EyebrowContext);
}

/** The header's callback ref for its segmented-control slot. */
export function useHeaderSlotRef(): (slot: HTMLElement | null) => void {
  const setters = useContext(SettersContext);
  if (!setters) {
    throw new Error("The header slot belongs inside ShellHeaderProvider.");
  }
  return setters.setSlot;
}

/** The header's callback ref for the slot on the title's own line, where a page's tabs go. */
export function useHeaderTabsSlotRef(): (slot: HTMLElement | null) => void {
  const setters = useContext(SettersContext);
  if (!setters) {
    throw new Error("The header slot belongs inside ShellHeaderProvider.");
  }
  return setters.setTabsSlot;
}

/** The header's callback ref for the slot on the title's own line, at the right, where a page's own buttons go. */
export function useHeaderButtonsSlotRef(): (slot: HTMLElement | null) => void {
  const setters = useContext(SettersContext);
  if (!setters) {
    throw new Error("The header slot belongs inside ShellHeaderProvider.");
  }
  return setters.setButtonsSlot;
}

/**
 * Sets the line above the header's title for as long as the calling page is mounted. For a page whose
 * line depends on what it has loaded; outside the shell it does nothing.
 */
export function useSetPageEyebrow(eyebrow: string | undefined): void {
  const setters = useContext(SettersContext);
  const setEyebrow = setters?.setEyebrow;
  useEffect(() => {
    if (!setEyebrow || !eyebrow) return undefined;
    setEyebrow(eyebrow);
    return () => setEyebrow(null);
  }, [setEyebrow, eyebrow]);
}

/**
 * Puts a page's own control (a segmented choice, a picker) in the header, beside the title. Rendered
 * outside the shell, where no header exists, it stays where it is written.
 */
export function ShellHeaderSlot({ children }: { children: ReactNode }) {
  const slot = useContext(SlotContext);
  const inShell = useContext(SettersContext) !== null;
  if (!inShell) return <>{children}</>;
  return slot ? createPortal(children, slot) : null;
}

/**
 * Puts a page's tabs on the header's title line, right after the title. Rendered outside the shell, where
 * no header exists, they stay where they are written.
 */
export function ShellHeaderTabs({ children }: { children: ReactNode }) {
  const slot = useContext(TabsSlotContext);
  const inShell = useContext(SettersContext) !== null;
  if (!inShell) return <>{children}</>;
  return slot ? createPortal(children, slot) : null;
}

/**
 * Puts a page's own buttons on the header's title line, at the right, just before Pause. Rendered outside the
 * shell, where no header exists, they stay where they are written.
 */
export function ShellHeaderButtons({ children }: { children: ReactNode }) {
  const slot = useContext(ButtonsSlotContext);
  const inShell = useContext(SettersContext) !== null;
  if (!inShell) return <>{children}</>;
  return slot ? createPortal(children, slot) : null;
}

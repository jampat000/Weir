/**
 * Unsaved edits in Settings are never dropped without asking. A panel with edits not yet saved names
 * them through `useUnsavedChanges`; the Settings page asks before its tab changes or the page is
 * left, and a panel asks through `useLeaveConfirmation` before it switches profile or closes.
 */
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useState,
  type ReactNode,
} from "react";

import { ConfirmDialog } from "../../components/ui/confirm-dialog";

/** Records that the panel under `key` has unsaved edits to `thing`, or none (null). */
type Register = (key: string, thing: string | null) => void;

const UnsavedChangesContext = createContext<Register | null>(null);

/** The question every guard asks, naming what would be lost. */
export function leaveQuestion(thing: string): string {
  return `You have unsaved changes to ${thing}. Leave without saving?`;
}

/** The confirmation itself. Staying is the safe choice, so it has the focus. */
export function LeaveWithoutSavingDialog({
  thing,
  onStay,
  onLeave,
}: {
  thing: string;
  onStay: () => void;
  onLeave: () => void;
}) {
  return (
    <ConfirmDialog
      testId="settings-unsaved-changes"
      title={leaveQuestion(thing)}
      confirmLabel="Leave without saving"
      cancelLabel="Keep editing"
      onCancel={onStay}
      onConfirm={onLeave}
    />
  );
}

/**
 * What every panel under the page has unsaved, named by the first of them, or null. While anything
 * is, closing or reloading the browser tab asks too.
 */
export function useUnsavedChangesRegistry(): {
  unsaved: string | null;
  register: Register;
} {
  const [things, setThings] = useState<ReadonlyMap<string, string>>(
    () => new Map(),
  );
  const register = useCallback<Register>((key, thing) => {
    setThings((current) => {
      if ((current.get(key) ?? null) === thing) return current;
      const next = new Map(current);
      if (thing === null) next.delete(key);
      else next.set(key, thing);
      return next;
    });
  }, []);
  const first = things.values().next();
  const unsaved = first.done ? null : first.value;

  useEffect(() => {
    if (unsaved === null) return undefined;
    const ask = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", ask);
    return () => window.removeEventListener("beforeunload", ask);
  }, [unsaved]);

  return { unsaved, register };
}

export function UnsavedChangesScope({
  register,
  children,
}: {
  register: Register;
  children: ReactNode;
}) {
  return (
    <UnsavedChangesContext.Provider value={register}>
      {children}
    </UnsavedChangesContext.Provider>
  );
}

/**
 * Tells the Settings page that `thing` has edits not yet saved, or that nothing does (null). Outside
 * the page, as in a panel's own tests, it does nothing.
 */
export function useUnsavedChanges(thing: string | null): void {
  const register = useContext(UnsavedChangesContext);
  const key = useId();
  useEffect(() => {
    register?.(key, thing);
  }, [register, key, thing]);
  useEffect(() => () => register?.(key, null), [register, key]);
}

/**
 * Asks before a panel drops its own unsaved edits. `confirmLeave(thing, leave)` runs `leave` at once
 * when `thing` is null, and otherwise only once the person chooses to leave. `dialog` goes wherever
 * the panel renders.
 */
export function useLeaveConfirmation(): {
  confirmLeave: (thing: string | null, leave: () => void) => void;
  dialog: ReactNode;
} {
  const [pending, setPending] = useState<{
    thing: string;
    leave: () => void;
  } | null>(null);
  const confirmLeave = useCallback(
    (thing: string | null, leave: () => void) => {
      if (thing === null) leave();
      else setPending({ thing, leave });
    },
    [],
  );
  const dialog = pending ? (
    <LeaveWithoutSavingDialog
      thing={pending.thing}
      onStay={() => setPending(null)}
      onLeave={() => {
        setPending(null);
        pending.leave();
      }}
    />
  ) : null;
  return { confirmLeave, dialog };
}

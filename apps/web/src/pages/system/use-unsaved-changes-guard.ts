import { useEffect } from "react";
import { useBlocker } from "react-router-dom";

/**
 * While `dirty`, leaving for another screen waits on the returned blocker (the caller asks), and
 * closing or reloading the tab gets the browser's own prompt. Moving between tabs of the same
 * screen is not leaving, so it is never blocked.
 */
export function useUnsavedChangesGuard(dirty: boolean) {
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      dirty && currentLocation.pathname !== nextLocation.pathname,
  );

  useEffect(() => {
    if (!dirty) return undefined;
    const holdUnload = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", holdUnload);
    return () => window.removeEventListener("beforeunload", holdUnload);
  }, [dirty]);

  return blocker;
}

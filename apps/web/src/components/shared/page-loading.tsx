import { createContext, useContext, type ReactNode } from "react";

/** True under the app shell's `<main>`, where a loading state must not start a page of its own. */
const InsideMain = createContext(false);

/** Marks everything under it as already inside the page's `<main>`. */
export function MainLandmark({ children }: { children: ReactNode }) {
  return <InsideMain.Provider value>{children}</InsideMain.Provider>;
}

function LoadingStatus({
  label,
  className,
}: {
  label: string;
  className: string;
}) {
  return (
    <div className={className} role="status" aria-live="polite">
      <div className="mm-loading-dots" aria-hidden="true">
        <span className="mm-loading-dot" />
        <span className="mm-loading-dot" />
        <span className="mm-loading-dot" />
      </div>
      <span>{label}</span>
    </div>
  );
}

/**
 * Loading dots with a label, in the space of the panel they stand in for, so the header, tabs and
 * other panels around them stay put while it loads (#697).
 */
export function PanelLoading({ label = "Loading…" }: { label?: string }) {
  return (
    <LoadingStatus label={label} className="mm-loading mm-loading--inline" />
  );
}

/**
 * A whole page that is still loading, for the screens outside the app shell (sign-in, setup). Under
 * the shell's `<main>` it is the panel version instead, so there is never a second `<main>`.
 */
export function PageLoading({ label = "Loading…" }: { label?: string }) {
  const insideMain = useContext(InsideMain);
  if (insideMain) return <PanelLoading label={label} />;
  return (
    <main className="mm-auth-body" id="mm-main-content" tabIndex={-1}>
      <LoadingStatus label={label} className="mm-loading" />
    </main>
  );
}

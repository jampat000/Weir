import { isLikelyNetworkFailure } from "../../lib/api/error-guards";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { DeveloperEntryError } from "./developer-entry-error";

/**
 * A screen that could not load at all (sign-in, setup, Processing, Library). A person running Weir
 * sees what to do in plain words and a Reload button; the server's text, status codes and developer
 * instructions stay out of it (#694). `vite dev` shows the developer help instead, and a production
 * build leaves that help out entirely.
 */
export function ApiEntryError({ error }: { error: unknown }) {
  if (import.meta.env.DEV) return <DeveloperEntryError error={error} />;
  const unreachable = isLikelyNetworkFailure(error);
  return (
    <>
      <h1 className="mm-auth-title mm-auth-title--alert">
        {unreachable ? "Can't reach Weir" : "Weir hit a problem"}
      </h1>
      <p className="mm-auth-lead">
        {unreachable
          ? "Make sure it's running (the tray icon on Windows, the container on Docker), then reload."
          : "Weir couldn't load this. Reload the page. If it keeps happening, System › Logs says why."}
      </p>
      <button
        type="button"
        className={mmActionButtonClass({ variant: "primary" })}
        onClick={() => window.location.reload()}
      >
        Reload
      </button>
    </>
  );
}

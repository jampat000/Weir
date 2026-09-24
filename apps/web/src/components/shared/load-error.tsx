import { loadErrorMessage } from "../../lib/api/error-message";

/**
 * Said in place of a panel whose data could not be loaded, so a failed load never looks like an
 * empty list. `thing` finishes "Weir couldn't load …", as in "these settings" or "your libraries".
 */
export function LoadError({
  thing,
  error,
}: {
  thing: string;
  error?: unknown;
}) {
  return (
    <p className="mm-status-text--failed text-sm" role="alert">
      {loadErrorMessage(error, thing)}
    </p>
  );
}

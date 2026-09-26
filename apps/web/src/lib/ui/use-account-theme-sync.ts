import { useEffect } from "react";
import { applyAccountAppTheme, type AppTheme } from "./app-theme";

/**
 * Applies the signed-in account's theme once `/auth/me` has answered, overriding a stale local
 * choice from another browser, address or tab (#790). Pass `undefined` while that answer is still
 * unknown (signed out, or the query has not resolved yet) so this leaves whatever the first-paint
 * cache already applied alone.
 */
export function useAccountThemeSync(theme: AppTheme | null | undefined): void {
  useEffect(() => {
    if (theme === undefined) return;
    applyAccountAppTheme(theme);
  }, [theme]);
}

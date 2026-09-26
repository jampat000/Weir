import { useEffect, useRef } from "react";
import {
  persistAppTheme,
  readStoredAppTheme,
  type AppTheme,
} from "./app-theme";

/**
 * Reconciles the signed-in account's theme with this browser's cached one, once `/auth/me` has
 * answered. Pass `undefined` while that answer is still unknown (signed out, or the query has not
 * resolved yet) so this leaves whatever the first-paint cache already applied alone.
 *
 * The account's preference wins when it has one. When it does not — including right after an
 * upgrade, when every account starts with no preference even for someone who already picked a
 * theme in this browser — that stale-looking null is never treated as "switch to the system
 * setting": a local choice is left alone and adopted onto the account instead, through `onAdopt`
 * (the same save the theme switch itself uses). Only when neither the account nor this browser has
 * a preference does the page keep following the system setting.
 */
export function useAccountThemeSync(
  theme: AppTheme | null | undefined,
  onAdopt: (theme: AppTheme) => void,
): void {
  const adopted = useRef(false);

  useEffect(() => {
    if (theme === undefined) return;
    if (theme) {
      persistAppTheme(theme);
      return;
    }

    // Runs at most once per sign-in: a save already sent stays sent even if /auth/me is refetched
    // again before the server's answer replaces this cached null.
    if (adopted.current) return;
    const local = readStoredAppTheme();
    if (local) {
      adopted.current = true;
      onAdopt(local);
    }
  }, [theme, onAdopt]);
}

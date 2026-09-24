import { fetchMe } from "../lib/api/auth-api";
import { authKeys } from "../lib/auth/query-keys";
import { fetchAppSettings } from "../lib/settings/settings-api";
import { settingsKeys } from "../lib/settings/query-keys";
import { queryClient } from "./query-client";

/**
 * Starts the two requests almost every screen needs — who is signed in, and the app's own settings
 * — at the same time as /ready, instead of waiting for the server to report ready and only then
 * discovering the app needs these too. Both use the same query keys `useMeQuery` and
 * `useAppSettingsQuery` read, so the screen that mounts once StartupGate clears finds the answer
 * already in the cache instead of asking again (#719).
 */
export function prefetchOnBoot(): void {
  void queryClient.prefetchQuery({
    queryKey: authKeys.me,
    queryFn: fetchMe,
    retry: false,
  });
  void queryClient.prefetchQuery({
    queryKey: settingsKeys.app,
    queryFn: fetchAppSettings,
  });
}

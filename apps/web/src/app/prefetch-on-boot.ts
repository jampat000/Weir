import { fetchMe } from "../lib/api/auth-api";
import { authKeys } from "../lib/auth/query-keys";
import { fetchAppSettings } from "../lib/settings/settings-api";
import { settingsKeys } from "../lib/settings/query-keys";
import { queryClient } from "./query-client";

/**
 * Who is signed in, then — only once that answer is a real person — the app's own settings, which a
 * signed-out visitor is never allowed to read. Both run through the same query keys `useMeQuery` and
 * `useAppSettingsQuery` read, so the screen that mounts once StartupGate clears finds the answer
 * already in the cache instead of asking again. Settings waits on `/auth/me` rather than racing it,
 * so a signed-out boot never makes a request this app already knows will be refused (#719).
 */
export function prefetchOnBoot(): void {
  void prefetchSettingsOnceSignedIn();
}

async function prefetchSettingsOnceSignedIn(): Promise<void> {
  const user = await queryClient
    .fetchQuery({ queryKey: authKeys.me, queryFn: fetchMe, retry: false })
    .catch(() => null);
  if (!user) {
    return;
  }
  void queryClient.prefetchQuery({
    queryKey: settingsKeys.app,
    queryFn: fetchAppSettings,
  });
}

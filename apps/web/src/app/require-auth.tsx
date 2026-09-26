import { Navigate, Outlet } from "react-router-dom";
import { PageLoading } from "../components/shared/page-loading";
import { useMeQuery, useSetThemeMutation } from "../lib/auth/queries";
import { sessionWasNotKept } from "../lib/auth/session-kept";
import { useAccountThemeSync } from "../lib/ui/use-account-theme-sync";

/** Everything past sign-in: a signed-out visitor goes to the login page, with the reason when a session did not stick. */
export function RequireAuth() {
  const me = useMeQuery();
  const setTheme = useSetThemeMutation();
  // Only once /auth/me has actually answered with a signed-in user: reconciling it any earlier
  // would fight the first-paint cache before there is a real account to reconcile with.
  useAccountThemeSync(
    me.data ? (me.data.app_theme ?? null) : undefined,
    setTheme.mutate,
  );
  if (me.isPending) {
    return <PageLoading />;
  }
  if (!me.data) {
    // A sign-in that succeeded seconds ago and is already unauthenticated means the browser
    // rejected the session cookie. Redirecting silently makes that indistinguishable from a
    // wrong password, so carry the reason across (#453).
    const to = sessionWasNotKept()
      ? "/login?problem=session-not-kept"
      : "/login";
    return <Navigate to={to} replace />;
  }
  return <Outlet />;
}

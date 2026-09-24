import { QueryClientProvider } from "@tanstack/react-query";
import { type ReactNode, useEffect } from "react";
import { setUnauthorizedHandler } from "../lib/api/client";
import { authKeys } from "../lib/auth/query-keys";
import { queryClient } from "./query-client";

export function AppProviders({ children }: { children: ReactNode }) {
  useEffect(() => {
    setUnauthorizedHandler(() => {
      queryClient.setQueryData(authKeys.me, null);
      queryClient.setQueryData(authKeys.session, null);
      void queryClient.cancelQueries({ queryKey: authKeys.me });
      void queryClient.cancelQueries({ queryKey: authKeys.session });
      if (
        window.location.pathname !== "/login" &&
        window.location.pathname !== "/setup"
      ) {
        window.history.replaceState(null, "", "/login?session=expired");
        window.dispatchEvent(new PopStateEvent("popstate"));
      }
    });
    return () => setUnauthorizedHandler(null);
  }, []);

  return (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

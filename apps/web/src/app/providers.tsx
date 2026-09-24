import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { type ReactNode, useEffect, useState } from "react";
import { setUnauthorizedHandler } from "../lib/api/client";
import { authKeys } from "../lib/auth/query-keys";

export function AppProviders({ children }: { children: ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            retry: 1,
            refetchOnWindowFocus: true,
          },
          mutations: {
            retry: false,
          },
        },
      }),
  );
  useEffect(() => {
    setUnauthorizedHandler(() => {
      client.setQueryData(authKeys.me, null);
      client.setQueryData(authKeys.session, null);
      void client.cancelQueries({ queryKey: authKeys.me });
      void client.cancelQueries({ queryKey: authKeys.session });
      if (
        window.location.pathname !== "/login" &&
        window.location.pathname !== "/setup"
      ) {
        window.history.replaceState(null, "", "/login?session=expired");
        window.dispatchEvent(new PopStateEvent("popstate"));
      }
    });
    return () => setUnauthorizedHandler(null);
  }, [client]);

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

import { QueryClient } from "@tanstack/react-query";

/**
 * One client for the whole app, created when this module loads rather than inside a component, so
 * main.tsx can start the first requests through it (see prefetch-on-boot.ts) before React has
 * rendered anything (#719).
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      // Freshness comes from the live stream (see lib/live); refocusing the tab is only an extra read.
      refetchOnWindowFocus: true,
    },
    mutations: {
      retry: false,
    },
  },
});

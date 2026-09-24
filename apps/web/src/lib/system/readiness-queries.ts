import { useQuery } from "@tanstack/react-query";

import { fetchSystemReadiness } from "./readiness-api";

import { systemKeys } from "./query-keys";

export function useSystemReadinessQuery() {
  return useQuery({
    queryKey: systemKeys.readiness,
    queryFn: fetchSystemReadiness,
    staleTime: 30_000,
    // Workers can stop while the screen is open, and the shell shows the version.
    refetchInterval: 60_000,
  });
}

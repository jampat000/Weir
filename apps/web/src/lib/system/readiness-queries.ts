import { useQuery } from "@tanstack/react-query";

import { fetchSystemReadiness } from "./readiness-api";

import { systemKeys } from "./query-keys";

/** Whether Weir is ready, and its version. The server says on `readiness` when a worker stops or a start finishes. */
export function useSystemReadinessQuery() {
  return useQuery({
    queryKey: systemKeys.readiness,
    queryFn: fetchSystemReadiness,
  });
}

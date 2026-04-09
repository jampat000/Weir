import { useQuery } from "@tanstack/react-query";
import { fetchFetcherOperationalOverview } from "../api/fetcher-api";

export const fetcherOverviewKey = ["fetcher", "overview"] as const;

export function useFetcherOperationalOverviewQuery() {
  return useQuery({
    queryKey: fetcherOverviewKey,
    queryFn: fetchFetcherOperationalOverview,
    staleTime: 30_000,
  });
}

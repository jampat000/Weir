import { useQuery } from "@tanstack/react-query";
import { fetchRefinerRuntimeSettings } from "./runtime-settings-api";

export const refinerRuntimeSettingsQueryKey = ["refiner", "runtime-settings"] as const;

export function useRefinerRuntimeSettingsQuery() {
  return useQuery({
    queryKey: refinerRuntimeSettingsQueryKey,
    queryFn: () => fetchRefinerRuntimeSettings(),
    staleTime: 30_000,
  });
}

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import {
  fetchDirectPlayDevices,
  putDirectPlayDevices,
} from "./direct-play-api";
import { processingKeys } from "./query-keys";

export function useDirectPlayDevicesQuery() {
  return useQuery({
    queryKey: processingKeys.directPlayDevices,
    queryFn: () => fetchDirectPlayDevices(),
    staleTime: 30_000,
  });
}

export function useDirectPlayDevicesSaveMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (selected: string[]) => putDirectPlayDevices(selected),
    onSuccess: (data) => {
      qc.setQueryData(processingKeys.directPlayDevices, data);
      // The badge on every file row answers for the chosen devices.
      void qc.invalidateQueries({ queryKey: processingKeys.files });
    },
  });
}

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import {
  fetchProcessingMetadataProvider,
  putProcessingMetadataProvider,
  testProcessingMetadataProvider,
  type ProcessingMetadataProviderWrite,
} from "./metadata-provider-api";
import { processingKeys } from "./query-keys";

export function useProcessingMetadataProviderQuery() {
  return useQuery({
    queryKey: processingKeys.metadataProvider,
    queryFn: fetchProcessingMetadataProvider,
  });
}

export function useSaveProcessingMetadataProvider() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (data: ProcessingMetadataProviderWrite) =>
      putProcessingMetadataProvider(data),
    onSuccess: (data) =>
      client.setQueryData(processingKeys.metadataProvider, data),
  });
}

export function useTestProcessingMetadataProvider() {
  return useMutation({
    mutationFn: (data: ProcessingMetadataProviderWrite) =>
      testProcessingMetadataProvider(data),
  });
}

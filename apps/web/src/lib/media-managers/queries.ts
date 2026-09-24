import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import {
  createMediaManagerConnection,
  deleteMediaManagerConnection,
  fetchMediaManagerConnections,
  generateMediaManagerWebhookSecret,
  testMediaManagerConnection,
  updateMediaManagerConnection,
  type MediaManagerConnection,
  type MediaManagerConnectionCreate,
  type MediaManagerConnectionUpdate,
} from "./media-managers-api";

import { mediaManagerKeys } from "./query-keys";

export function useMediaManagerConnectionsQuery(enabled = true) {
  return useQuery<MediaManagerConnection[]>({
    queryKey: mediaManagerKeys.connections,
    queryFn: fetchMediaManagerConnections,
    enabled,
  });
}

export function useCreateMediaManagerConnection() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: MediaManagerConnectionCreate) =>
      createMediaManagerConnection(data),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: mediaManagerKeys.connections }),
  });
}

export function useUpdateMediaManagerConnection() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { id: number; data: MediaManagerConnectionUpdate }) =>
      updateMediaManagerConnection(vars.id, vars.data),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: mediaManagerKeys.connections }),
  });
}

export function useDeleteMediaManagerConnection() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => deleteMediaManagerConnection(id),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: mediaManagerKeys.connections }),
  });
}

export function useTestMediaManagerConnection() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => testMediaManagerConnection(id),
    // The test result is stored on the row, so the list is now stale.
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: mediaManagerKeys.connections }),
  });
}

export function useGenerateMediaManagerWebhookSecret() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => generateMediaManagerWebhookSecret(id),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: mediaManagerKeys.connections }),
  });
}

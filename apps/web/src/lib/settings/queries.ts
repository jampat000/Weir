import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createNotificationChannel,
  deleteNotificationChannel,
  fetchConfigurationBackupList,
  fetchNotificationChannels,
  fetchServerMetrics,
  fetchSecurityOverview,
  fetchAppSettings,
  fetchNetworkAccess,
  putNetworkAccess,
  fetchUpdateStatus,
  fetchUpdateSettings,
  fetchUpdateState,
  postApplyUpdate,
  putAppSettings,
  putUpdateSettings,
  resetOperationalHistory,
  testNotificationChannel,
  updateNotificationChannel,
} from "./settings-api";
import { settingsKeys } from "./query-keys";
import type {
  AppSettingsPutBody,
  NetworkAccessPutBody,
  NotificationChannelIn,
  UpdateSettingsPutBody,
} from "./types";

export function useAppSettingsQuery() {
  return useQuery({
    queryKey: settingsKeys.app,
    queryFn: () => fetchAppSettings(),
    staleTime: 30_000,
  });
}

export function useSecurityOverviewQuery() {
  return useQuery({
    queryKey: settingsKeys.securityOverview,
    queryFn: () => fetchSecurityOverview(),
    staleTime: 30_000,
  });
}

export function useConfigurationBackupsQuery(enabled: boolean) {
  return useQuery({
    queryKey: settingsKeys.configurationBackups,
    queryFn: () => fetchConfigurationBackupList(),
    enabled,
    staleTime: 15_000,
  });
}

/**
 * System › About's network-reach state. Independent of every other card on the page (ux-common: no panel waits on
 * another's data). The server says when the tray has answered (the `network_access` topic), so a change that waits for
 * the tray, or a retry of the firewall step, shows its outcome without asking.
 */
export function useNetworkAccessQuery() {
  return useQuery({
    queryKey: settingsKeys.networkAccess,
    queryFn: () => fetchNetworkAccess(),
    staleTime: 30_000,
    retry: false,
  });
}

export function useNetworkAccessMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: NetworkAccessPutBody) => putNetworkAccess(body),
    onSuccess: (data) => {
      qc.setQueryData(settingsKeys.networkAccess, data);
    },
  });
}

export function useUpdateStatusQuery(enabled = true) {
  return useQuery({
    queryKey: settingsKeys.updateStatus,
    queryFn: () => fetchUpdateStatus(),
    enabled,
    staleTime: 60_000,
    retry: false,
  });
}

export function useUpdateStateQuery(enabled = true) {
  return useQuery({
    queryKey: settingsKeys.updateState,
    queryFn: () => fetchUpdateState(),
    enabled,
    staleTime: 0,
    retry: false,
  });
}

export function useApplyUpdateMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => postApplyUpdate(),
    onSuccess: (data) => {
      qc.setQueryData(settingsKeys.updateState, data);
    },
  });
}

export function useUpdateSettingsQuery(enabled = true) {
  return useQuery({
    queryKey: settingsKeys.updateSettings,
    queryFn: () => fetchUpdateSettings(),
    enabled,
    staleTime: 30_000,
    retry: false,
  });
}

export function useUpdateSettingsMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateSettingsPutBody) => putUpdateSettings(body),
    onSuccess: (data) => {
      qc.setQueryData(settingsKeys.updateSettings, data);
    },
  });
}

export function useHistoryResetMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (confirm: string) => resetOperationalHistory(confirm),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: settingsKeys.metrics });
    },
  });
}

export function useServerMetricsQuery(enabled = true) {
  return useQuery({
    queryKey: settingsKeys.metrics,
    queryFn: () => fetchServerMetrics(),
    enabled,
    staleTime: 5000,
    retry: false,
  });
}

export function useNotificationChannelsQuery(enabled = true) {
  return useQuery({
    queryKey: settingsKeys.notificationChannels,
    queryFn: () => fetchNotificationChannels(),
    enabled,
    staleTime: 30_000,
  });
}

export function useCreateNotificationChannelMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: NotificationChannelIn) =>
      createNotificationChannel(data),
    onSuccess: async () => {
      await qc.invalidateQueries({
        queryKey: settingsKeys.notificationChannels,
      });
    },
  });
}

export function useUpdateNotificationChannelMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: NotificationChannelIn }) =>
      updateNotificationChannel(id, data),
    onSuccess: async () => {
      await qc.invalidateQueries({
        queryKey: settingsKeys.notificationChannels,
      });
    },
  });
}

export function useDeleteNotificationChannelMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => deleteNotificationChannel(id),
    onSuccess: async () => {
      await qc.invalidateQueries({
        queryKey: settingsKeys.notificationChannels,
      });
    },
  });
}

export function useTestNotificationChannelMutation() {
  return useMutation({
    mutationFn: (id: number) => testNotificationChannel(id),
  });
}

export function useAppSettingsSaveMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: AppSettingsPutBody) => putAppSettings(body),
    onSuccess: async (data) => {
      qc.setQueryData(settingsKeys.app, data);
      await qc.invalidateQueries({ queryKey: settingsKeys.app });
    },
  });
}

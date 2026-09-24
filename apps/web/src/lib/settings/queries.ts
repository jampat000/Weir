import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createNotificationChannel,
  deleteNotificationChannel,
  fetchConfigurationBackupList,
  fetchNotificationChannels,
  fetchServerLogs,
  fetchServerMetrics,
  fetchSecurityOverview,
  fetchAppSettings,
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
  NotificationChannelIn,
  ServerLogFilters,
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

export function useUpdateStatusQuery(
  enabled = true,
  refetchInterval: number | false = false,
) {
  return useQuery({
    queryKey: settingsKeys.updateStatus,
    queryFn: () => fetchUpdateStatus(),
    enabled,
    staleTime: enabled && refetchInterval ? 0 : 60_000,
    refetchInterval,
    retry: false,
  });
}

export function useUpdateStateQuery(enabled = true) {
  return useQuery({
    queryKey: settingsKeys.updateState,
    queryFn: () => fetchUpdateState(),
    enabled,
    staleTime: 0,
    refetchInterval: enabled ? 10_000 : false,
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

export function useServerLogsQuery(filters: ServerLogFilters, enabled = true) {
  return useQuery({
    queryKey: settingsKeys.logsFor(filters),
    queryFn: () => fetchServerLogs(filters),
    enabled,
    refetchInterval: enabled ? 5000 : false,
    staleTime: 2000,
    retry: false,
  });
}

export function useServerMetricsQuery(enabled = true) {
  return useQuery({
    queryKey: settingsKeys.metrics,
    queryFn: () => fetchServerMetrics(),
    enabled,
    staleTime: 5000,
    refetchInterval: enabled ? 10000 : false,
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

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

/** How often the state is read again while a change waits for the tray, or while someone retries the firewall step. */
const NETWORK_ACCESS_PENDING_POLL_MS = 2_000;

/**
 * System › About's network-reach state. Independent of every other card on the page (ux-common: no panel waits on
 * another's data). It is read again every couple of seconds while a change is pending, and while `watching` says a
 * retry of the firewall step is under way.
 */
export function useNetworkAccessQuery(watching = false) {
  return useQuery({
    queryKey: settingsKeys.networkAccess,
    queryFn: () => fetchNetworkAccess(),
    staleTime: 30_000,
    retry: false,
    refetchInterval: (query) =>
      watching || query.state.data?.pending_scope
        ? NETWORK_ACCESS_PENDING_POLL_MS
        : false,
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

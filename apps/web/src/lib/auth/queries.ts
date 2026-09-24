import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchBootstrapStatus,
  fetchCurrentSession,
  fetchActiveSessions,
  postChangePassword,
  postChangeUsername,
  fetchMe,
  postBootstrap,
  postLogin,
  postLogout,
  postRevokeOtherSessions,
  postRevokeSession,
} from "../api/auth-api";
import { markLoginSucceeded } from "./session-kept";
import { activityKeys } from "../activity/query-keys";
import { authKeys } from "./query-keys";

export function useMeQuery() {
  return useQuery({
    queryKey: authKeys.me,
    queryFn: fetchMe,
    retry: false,
  });
}

export function useBootstrapStatusQuery() {
  return useQuery({
    queryKey: authKeys.bootstrap,
    queryFn: fetchBootstrapStatus,
    retry: 1,
  });
}

export function useCurrentSessionQuery() {
  return useQuery({
    queryKey: authKeys.session,
    queryFn: fetchCurrentSession,
    retry: false,
  });
}

export function useActiveSessionsQuery(enabled = true) {
  return useQuery({
    queryKey: authKeys.sessions,
    queryFn: fetchActiveSessions,
    retry: false,
    enabled,
  });
}

export function useRevokeOtherSessionsMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: postRevokeOtherSessions,
    onSuccess: async () => {
      await Promise.all([
        qc.invalidateQueries({ queryKey: authKeys.sessions }),
        qc.invalidateQueries({ queryKey: activityKeys.recent }),
      ]);
    },
  });
}

export function useRevokeSessionMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (sessionId: string) => postRevokeSession(sessionId),
    onSuccess: async () => {
      await Promise.all([
        qc.invalidateQueries({ queryKey: authKeys.sessions }),
        qc.invalidateQueries({ queryKey: activityKeys.recent }),
      ]);
    },
  });
}

export function useLoginMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      username,
      password,
      trustedDevice,
    }: {
      username: string;
      password: string;
      trustedDevice: boolean;
    }) => postLogin(username, password, trustedDevice),
    onSuccess: (data) => {
      // Remember that the server accepted us, so a 401 moments later can be reported as a
      // rejected cookie rather than a silent bounce back to the form (#453).
      markLoginSucceeded();
      // Anonymous /me is cached as `null` (401). Hydrate from the login response — do not invalidate
      // /me here: an immediate refetch can run before the session cookie is visible to fetch(), get
      // 401, and overwrite this cache back to null (E2E/CI flake).
      qc.setQueryData(authKeys.me, data.user);
      void qc.invalidateQueries({ queryKey: authKeys.bootstrap });
      void qc.invalidateQueries({ queryKey: authKeys.session });
      void qc.invalidateQueries({ queryKey: activityKeys.recent });
    },
  });
}

export function useLogoutMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: postLogout,
    onMutate: async () => {
      await qc.cancelQueries({ queryKey: authKeys.me });
      await qc.cancelQueries({ queryKey: authKeys.session });
      qc.setQueryData(authKeys.me, null);
      qc.setQueryData(authKeys.session, null);
    },
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: authKeys.me });
      void qc.invalidateQueries({ queryKey: authKeys.session });
      void qc.invalidateQueries({ queryKey: authKeys.bootstrap });
      void qc.invalidateQueries({ queryKey: activityKeys.recent });
    },
  });
}

export function useBootstrapMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      username,
      password,
      setupCode,
    }: {
      username: string;
      password: string;
      setupCode?: string;
    }) => postBootstrap(username, password, setupCode),
    onSuccess: (data) => {
      // Creating the account also signs it in (#704): hydrate /me from the response, the same way
      // sign-in does, rather than invalidating it and racing the new cookie's first request.
      if (data.user) {
        markLoginSucceeded();
        qc.setQueryData(authKeys.me, data.user);
      }
      void qc.invalidateQueries({ queryKey: authKeys.bootstrap });
      void qc.invalidateQueries({ queryKey: authKeys.session });
      void qc.invalidateQueries({ queryKey: activityKeys.recent });
    },
  });
}

export function useChangeUsernameMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      currentPassword,
      newUsername,
    }: {
      currentPassword: string;
      newUsername: string;
    }) => postChangeUsername(currentPassword, newUsername),
    onSuccess: () => {
      // The session is untouched by a rename, so only the displayed identity needs refreshing.
      void qc.invalidateQueries({ queryKey: authKeys.me });
      void qc.invalidateQueries({ queryKey: activityKeys.recent });
    },
  });
}

export function useChangePasswordMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      currentPassword,
      newPassword,
    }: {
      currentPassword: string;
      newPassword: string;
    }) => postChangePassword(currentPassword, newPassword),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: authKeys.me });
      void qc.invalidateQueries({ queryKey: authKeys.session });
      void qc.invalidateQueries({ queryKey: activityKeys.recent });
    },
  });
}

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { activityRecentKey } from "../../activity/queries";
import {
  fetchFetcherArrOperatorSettings,
  postFetcherArrConnectionTest,
  putFetcherArrConnectionRadarr,
  putFetcherArrConnectionSonarr,
  putFetcherArrSearchLane,
} from "./arr-operator-settings-api";
import type {
  FetcherArrConnectionPutBody,
  FetcherArrConnectionTestBody,
  FetcherArrSearchLane,
  FetcherArrSearchLaneKey,
} from "./types";

export const fetcherArrOperatorSettingsQueryKey = ["fetcher", "arr-operator-settings"] as const;

export function useFetcherArrOperatorSettingsQuery() {
  return useQuery({
    queryKey: fetcherArrOperatorSettingsQueryKey,
    queryFn: fetchFetcherArrOperatorSettings,
    staleTime: 30_000,
  });
}

export function useFetcherArrSearchLaneSaveMutation(laneKey: FetcherArrSearchLaneKey) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (lane: FetcherArrSearchLane) => putFetcherArrSearchLane(laneKey, lane),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: fetcherArrOperatorSettingsQueryKey });
    },
  });
}

export function useFetcherArrConnectionTestMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: FetcherArrConnectionTestBody) => postFetcherArrConnectionTest(body),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: activityRecentKey });
      void qc.invalidateQueries({ queryKey: fetcherArrOperatorSettingsQueryKey });
    },
  });
}

export function useFetcherArrConnectionSonarrSaveMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: FetcherArrConnectionPutBody) => putFetcherArrConnectionSonarr(body),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: fetcherArrOperatorSettingsQueryKey });
    },
  });
}

export function useFetcherArrConnectionRadarrSaveMutation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: FetcherArrConnectionPutBody) => putFetcherArrConnectionRadarr(body),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: fetcherArrOperatorSettingsQueryKey });
    },
  });
}

import { useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";

import { subscribeSystemTasks } from "../activity/use-activity-stream-invalidation";
import { apiFetch, readJson, requireOk } from "../api/client";
import { systemKeys } from "./query-keys";
import { parseSystemTasks } from "./system-tasks-frame";
import type { SystemTask } from "./system-tasks-frame";

const tasksPath = "/api/v1/system/tasks";

export async function fetchSystemTasks(): Promise<SystemTask[]> {
  const response = await apiFetch(tasksPath);
  await requireOk(tasksPath, response, "Could not read Weir's scheduled tasks");
  return parseSystemTasks(await readJson<unknown>(response)) ?? [];
}

/** Every periodic task, read once and then kept current by the `system.tasks` frames the server pushes. */
export function useSystemTasksQuery() {
  const queryClient = useQueryClient();
  useEffect(
    () =>
      subscribeSystemTasks((tasks) =>
        queryClient.setQueryData(systemKeys.tasks, tasks),
      ),
    [queryClient],
  );
  return useQuery({
    queryKey: systemKeys.tasks,
    queryFn: fetchSystemTasks,
    // Frames reach the cache only while a card is listening, so a card that opens reads the list again whatever it kept.
    staleTime: 0,
    retry: false,
  });
}

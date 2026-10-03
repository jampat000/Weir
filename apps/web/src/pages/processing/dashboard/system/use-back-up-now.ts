import { useMutation, useQueryClient } from "@tanstack/react-query";

import { postConfigurationBackupNow } from "../../../../lib/settings/settings-api";
import { settingsKeys } from "../../../../lib/settings/query-keys";

/** Writes a configuration backup now, the same file the schedule writes, and brings the lists that show it up to date. */
export function useBackUpNow() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: postConfigurationBackupNow,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: settingsKeys.configurationBackups,
        }),
        queryClient.invalidateQueries({ queryKey: settingsKeys.app }),
      ]);
    },
  });
}

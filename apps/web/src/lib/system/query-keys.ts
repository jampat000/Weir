/** Every query key about the running server itself. */
export const systemKeys = {
  readiness: ["system", "readiness"] as const,
  mediaTools: ["system", "media-tools"] as const,
  stats: ["system", "stats"] as const,
  overview: ["system", "overview"] as const,
  tasks: ["system", "tasks"] as const,
  logLines: (level: string) => ["system", "log", level] as const,
  /** Every page of System › Logs, whatever it is filtered by. */
  logEntriesAll: ["system", "log-entries"] as const,
  logEntries: (query: unknown) => ["system", "log-entries", query] as const,
};

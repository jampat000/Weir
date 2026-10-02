/** Every query key about the running server itself. */
export const systemKeys = {
  readiness: ["system", "readiness"] as const,
  mediaTools: ["system", "media-tools"] as const,
  stats: ["system", "stats"] as const,
  overview: ["system", "overview"] as const,
  tasks: ["system", "tasks"] as const,
  logLines: (level: string) => ["system", "log", level] as const,
};

/** Every query key about the running server itself. */
export const systemKeys = {
  readiness: ["system", "readiness"] as const,
  mediaTools: ["system", "media-tools"] as const,
  stats: ["system", "stats"] as const,
  overview: ["system", "overview"] as const,
};

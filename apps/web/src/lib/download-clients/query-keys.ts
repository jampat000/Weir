/** Every download-client query key. */
export const downloadClientKeys = {
  connections: ["download-clients", "connections"] as const,
  suggestions: (mediaType: "movie" | "tv") =>
    ["download-clients", "suggestions", mediaType] as const,
};

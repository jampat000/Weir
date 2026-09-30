/** Longest nickname the server keeps for a media manager or download client connection. */
export const CONNECTION_NICKNAME_MAX_LENGTH = 30;

/** What separates a connection's name from its nickname wherever both are shown. */
const NICKNAME_SEPARATOR = " · ";

/**
 * A connection as people read it: the name Weir derives from where it runs, then the nickname a person gave it,
 * "Radarr on nas · 4K". A connection with no nickname reads as its name alone.
 */
export function connectionTitle(connection: {
  name: string;
  nickname?: string | null;
}): string {
  const nickname = connection.nickname?.trim();
  return nickname
    ? `${connection.name}${NICKNAME_SEPARATOR}${nickname}`
    : connection.name;
}

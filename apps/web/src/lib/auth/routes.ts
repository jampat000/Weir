import type { UserPublic } from "../api/types";

/**
 * Authenticated product shell is mounted at `/app` with child routes such as
 * `/app/activity`, suite tools (`/app/fetcher`, …), and `/app/settings`.
 * Entry routing only needs the shell root; individual paths live in the app router.
 */

export type EntryDecision =
  | { kind: "wait" }
  | { kind: "redirect"; to: "/app" | "/setup" | "/login" };

/**
 * Where to send the user on initial load: dashboard, first-run setup, or login.
 * Pure helper — tested without React.
 */
export function resolveEntryDecision(args: {
  meLoading: boolean;
  bootstrapLoading: boolean;
  user: UserPublic | null | undefined;
  bootstrapAllowed: boolean | undefined;
}): EntryDecision {
  if (args.meLoading || args.bootstrapLoading) {
    return { kind: "wait" };
  }
  if (args.user) {
    return { kind: "redirect", to: "/app" };
  }
  if (args.bootstrapAllowed === true) {
    return { kind: "redirect", to: "/setup" };
  }
  return { kind: "redirect", to: "/login" };
}

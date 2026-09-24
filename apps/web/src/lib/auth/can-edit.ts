import { useMeQuery } from "./queries";

/** Operators and admins change settings and files; viewers only read. */
export function canEdit(role: string | undefined): boolean {
  return role === "operator" || role === "admin";
}

/** Whether the signed-in user may change things. False until the session has loaded. */
export function useCanEdit(): boolean {
  return canEdit(useMeQuery().data?.role);
}

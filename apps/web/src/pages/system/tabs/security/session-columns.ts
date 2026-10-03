import type { TableColumnsConfig } from "../../../../lib/ui/table-columns";

export type SessionColumnId = "browser" | "lastSeen" | "expires" | "signOut";

/** The browsers signed in: all of them are loaded, so the browser sorts them, in the order the server gives until told. */
export const SESSION_COLUMNS: TableColumnsConfig<SessionColumnId> = {
  tableId: "system-sessions",
  sortable: true,
  defaultSort: null,
  columns: [
    { id: "browser", label: "Browser" },
    { id: "lastSeen", label: "Last seen", firstDirection: "desc" },
    { id: "expires", label: "Expires" },
    { id: "signOut", label: "Sign out", movable: false, sortable: false },
  ],
};

/** Wire tokens stored per scope (API + backend). */
export const PRUNER_PEOPLE_ROLE_IDS = ["cast", "director", "writer", "producer", "guest_star"] as const;
export type PrunerPeopleRoleId = (typeof PRUNER_PEOPLE_ROLE_IDS)[number];

const ROLE_LABELS: Record<PrunerPeopleRoleId, string> = {
  cast: "Cast (actors)",
  director: "Directors",
  writer: "Writers",
  producer: "Producers",
  guest_star: "Guest stars",
};

const PLEX_UNAVAILABLE: ReadonlySet<PrunerPeopleRoleId> = new Set(["producer", "guest_star"]);

export const DEFAULT_PRUNER_PEOPLE_ROLES: PrunerPeopleRoleId[] = ["cast"];

export function normalizePeopleRolesFromApi(raw: string[] | undefined | null): PrunerPeopleRoleId[] {
  const out: PrunerPeopleRoleId[] = [];
  const seen = new Set<string>();
  for (const id of PRUNER_PEOPLE_ROLE_IDS) {
    if (raw?.some((x) => x.trim().toLowerCase() === id) && !seen.has(id)) {
      seen.add(id);
      out.push(id);
    }
  }
  return out.length ? out : [...DEFAULT_PRUNER_PEOPLE_ROLES];
}

/** Plex PATCH: omit tags Plex cannot supply (greyed UI). */
export function peopleRolesForPlexPersist(roles: readonly PrunerPeopleRoleId[]): PrunerPeopleRoleId[] {
  return roles.filter((r) => !PLEX_UNAVAILABLE.has(r));
}

type PrunerPeopleRoleCheckboxesProps = {
  value: PrunerPeopleRoleId[];
  onChange: (next: PrunerPeopleRoleId[]) => void;
  disabled: boolean;
  variant: "emby-jellyfin" | "plex";
  /** Shown when UI coerced empty selection back to cast. */
  coerceCastMsg: string | null;
  onClearCoerceMsg: () => void;
  onCoercedToCast?: () => void;
  testId?: string;
};

export function PrunerPeopleRoleCheckboxes({
  value,
  onChange,
  disabled,
  variant,
  coerceCastMsg,
  onClearCoerceMsg,
  onCoercedToCast,
  testId,
}: PrunerPeopleRoleCheckboxesProps) {
  const isPlex = variant === "plex";

  function toggle(id: PrunerPeopleRoleId) {
    onClearCoerceMsg();
    if (isPlex && PLEX_UNAVAILABLE.has(id)) return;
    const has = value.includes(id);
    const next = has ? value.filter((x) => x !== id) : [...value, id];
    if (next.length === 0) {
      onCoercedToCast?.();
      onChange([...DEFAULT_PRUNER_PEOPLE_ROLES]);
      return;
    }
    onChange(next);
  }

  const helper =
    variant === "plex"
      ? "Only people matching the selected roles will be used."
      : "Only people matching the selected roles will be used. Uses cast when nothing is selected.";

  return (
    <div className="space-y-2" data-testid={testId ?? "pruner-people-role-checkboxes"}>
      <p className="text-xs font-medium text-[var(--mm-text2)]">Match people in these credit roles</p>
      <div className="flex flex-col gap-2">
        {PRUNER_PEOPLE_ROLE_IDS.map((id) => {
          const checked = value.includes(id);
          const grey = isPlex && PLEX_UNAVAILABLE.has(id);
          const label =
            id === "producer" && grey
              ? "Producers (not available on Plex)"
              : id === "guest_star" && grey
                ? "Guest stars (not available on Plex)"
                : ROLE_LABELS[id];
          return (
            <label
              key={id}
              className={[
                "flex cursor-pointer items-start gap-2 rounded-md border px-2.5 py-2 text-sm transition-colors",
                grey
                  ? "cursor-not-allowed border-[var(--mm-border)]/60 bg-[var(--mm-surface2)]/20 text-[var(--mm-text3)]"
                  : checked
                    ? "border-[var(--mm-accent)]/50 bg-[var(--mm-accent-soft)]/35 text-[var(--mm-text1)]"
                    : "border-[var(--mm-border)] bg-[var(--mm-card-bg)] text-[var(--mm-text2)] hover:border-[var(--mm-text3)]/40",
                disabled || grey ? "pointer-events-none opacity-80" : "",
              ].join(" ")}
            >
              <input
                type="checkbox"
                className="mt-0.5 h-4 w-4 shrink-0 accent-[var(--mm-accent)]"
                checked={checked}
                disabled={disabled || grey}
                onChange={() => toggle(id)}
              />
              <span className={checked && !grey ? "font-medium" : ""}>{label}</span>
            </label>
          );
        })}
      </div>
      <p className="text-xs text-[var(--mm-text3)]">{helper}</p>
      {coerceCastMsg ? (
        <p className="text-xs text-amber-600/95" role="status">
          {coerceCastMsg}
        </p>
      ) : null}
    </div>
  );
}

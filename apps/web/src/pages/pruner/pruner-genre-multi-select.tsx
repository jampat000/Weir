/**
 * Hard-coded genre pick list for Pruner Rules (matches common Jellyfin / Emby / Plex genre strings).
 */

export const PRUNER_RULE_GENRE_OPTIONS = [
  "Action",
  "Adventure",
  "Animation",
  "Comedy",
  "Crime",
  "Documentary",
  "Drama",
  "Family",
  "Fantasy",
  "History",
  "Horror",
  "Music",
  "Mystery",
  "Romance",
  "Science Fiction",
  "Thriller",
  "War",
  "Western",
] as const;

/** Map saved API genres onto canonical list entries (case-insensitive). */
export function prunerGenresFromApi(api: string[] | undefined | null): string[] {
  if (!api?.length) return [];
  const out: string[] = [];
  for (const canon of PRUNER_RULE_GENRE_OPTIONS) {
    if (api.some((a) => a.trim().toLowerCase() === canon.toLowerCase())) {
      out.push(canon);
    }
  }
  return out;
}

export function PrunerGenreMultiSelect({
  value,
  onChange,
  disabled,
  testId,
}: {
  value: string[];
  onChange: (next: string[]) => void;
  disabled: boolean;
  testId?: string;
}) {
  const n = value.length;
  const summary = n === 0 ? "All genres" : `${n} genre${n === 1 ? "" : "s"} selected`;

  function toggle(g: string) {
    const lower = g.toLowerCase();
    const has = value.some((x) => x.toLowerCase() === lower);
    if (has) {
      onChange(value.filter((x) => x.toLowerCase() !== lower));
    } else {
      onChange([...value, g]);
    }
  }

  return (
    <div className="space-y-2" data-testid={testId ?? "pruner-genre-multiselect"}>
      <p className="text-xs font-medium text-[var(--mm-text2)]" data-testid="pruner-genre-multiselect-summary">
        {summary}
      </p>
      <p className="text-xs text-[var(--mm-text3)]">
        Leave none selected to include every genre. Pick one or more to limit scans to those genres only.
      </p>
      <div
        className="max-h-36 overflow-y-auto rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)] p-1"
        role="listbox"
        aria-multiselectable
        aria-label="Genres"
      >
        <div className="flex flex-col gap-1">
          {PRUNER_RULE_GENRE_OPTIONS.map((g) => {
            const selected = value.some((x) => x.toLowerCase() === g.toLowerCase());
            return (
              <label
                key={g}
                className={[
                  "flex cursor-pointer items-center gap-2.5 rounded-md border px-2.5 py-2 text-left text-sm transition-colors",
                  selected
                    ? "border-[var(--mm-accent)]/55 bg-[var(--mm-accent-soft)]/40 font-medium text-[var(--mm-text1)] shadow-sm"
                    : "border-transparent bg-[var(--mm-surface2)]/25 text-[var(--mm-text2)] hover:border-[var(--mm-border)] hover:bg-[var(--mm-surface2)]/50",
                  disabled ? "cursor-not-allowed opacity-50" : "",
                ].join(" ")}
              >
                <input
                  type="checkbox"
                  className="h-4 w-4 shrink-0 accent-[var(--mm-accent)]"
                  checked={selected}
                  disabled={disabled}
                  onChange={() => toggle(g)}
                />
                <span className="select-none">{g}</span>
              </label>
            );
          })}
        </div>
      </div>
    </div>
  );
}

import { Field } from "../../../../components/shared/field";
import type { ProcessingRuleSet } from "../../../../lib/processing/libraries-api";
import { plural } from "../../../../lib/ui/mm-plural";
import type { RuleSetBinding } from "./rule-set-fields";

/**
 * One line: which profile, its name, and who uses it. A new profile has no one using it yet, so the
 * line says where to choose it once it is saved.
 */
export function ProfileBar({
  ruleSets,
  selectedId,
  creating,
  binding,
  usedBy,
  onSelect,
}: {
  ruleSets: ProcessingRuleSet[];
  selectedId: number | null;
  creating: boolean;
  binding: RuleSetBinding | null;
  usedBy: string[];
  onSelect: (id: number) => void;
}) {
  return (
    <div className="mm-profile-bar" data-testid="rule-set-profile-bar">
      {ruleSets.length > 0 && !creating ? (
        <Field label="Profile" width="medium">
          <select
            className="mm-input"
            value={selectedId ?? ""}
            onChange={(event) => onSelect(Number(event.target.value))}
          >
            {ruleSets.map((row) => (
              <option key={row.id} value={row.id}>
                {row.name} ·{" "}
                {plural(row.used_by_library_count, "library", "libraries")}
              </option>
            ))}
          </select>
        </Field>
      ) : null}

      {binding ? (
        <Field label="Name" width="medium">
          <input
            className="mm-input"
            value={binding.draft.name}
            placeholder="English feature films"
            disabled={binding.disabled}
            onChange={(event) => binding.change("name", event.target.value)}
          />
        </Field>
      ) : null}
      {creating ? (
        <p className="mm-profile-bar__used">
          A new profile. Choose it in a library&rsquo;s editor once it is saved.
        </p>
      ) : binding ? (
        <p className="mm-profile-bar__used" data-testid="rule-set-used-by">
          {usedBy.length > 0
            ? `Used by ${usedBy.join(", ")}`
            : "No library uses it yet. Choose it in a library's editor."}
        </p>
      ) : null}
    </div>
  );
}

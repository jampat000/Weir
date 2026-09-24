import { QuietFieldGroup } from "../../../../components/shared/quiet-section";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import type { ProcessingRuleSet } from "../../../../lib/processing/rule-sets-api";
import {
  MEDIA_TYPE_OPTIONS,
  WRITER_HINTS,
  WRITER_OPTIONS,
} from "./library-options";
import {
  FolderSetting,
  SelectSetting,
  TextSetting,
  type LibraryFormBinding,
  type SettingOption,
} from "./library-settings";

/** A library left on "" gets its kind's default profile, so that is offered first until another is chosen. */
function ruleSetOptions(
  binding: LibraryFormBinding,
  ruleSets: ProcessingRuleSet[],
): SettingOption[] {
  const { form } = binding;
  const kindDefault =
    form.rule_set_id === ""
      ? [
          {
            value: "",
            label: `${form.media_type === "tv" ? "TV default" : "Movies default"} (the default for its kind)`,
          },
        ]
      : [];
  return [
    ...kindDefault,
    ...ruleSets.map((ruleSet) => ({
      value: String(ruleSet.id),
      label: ruleSet.name,
    })),
  ];
}

/** What the library is called, what it holds and which folders it uses. */
export function LibraryFoldersGroup({
  binding,
  ruleSets,
  connections,
}: {
  binding: LibraryFormBinding;
  ruleSets: ProcessingRuleSet[];
  connections: MediaManagerConnection[];
}) {
  return (
    <QuietFieldGroup
      title="Identity and folders"
      detail="One watched folder, one safe work area, and one finished output."
    >
      <div className="mm-field-row">
        <TextSetting
          binding={binding}
          name="name"
          label="Name"
          width="medium"
          placeholder="Movies 4K"
        />
        <SelectSetting
          binding={binding}
          name="media_type"
          label="Media type"
          options={MEDIA_TYPE_OPTIONS}
        />
        <SelectSetting
          binding={binding}
          name="rule_set_id"
          label="Rules profile"
          options={ruleSetOptions(binding, ruleSets)}
          hint="Create and edit profiles under Rules."
        />
        <SelectSetting
          binding={binding}
          name="manager_connection_id"
          label="Media manager"
          options={[
            { value: "", label: "None" },
            ...connections.map((c) => ({ value: String(c.id), label: c.name })),
          ]}
          hint="The media manager that sends this library its downloads. Weir waits for it to finish with a download, hands the cleaned file back, and can ask it for a different release when one is bad."
          testId="library-manager-choice"
        />
        <SelectSetting
          binding={binding}
          name="remux_writer"
          label="Writes files with"
          options={WRITER_OPTIONS}
          hint={WRITER_HINTS[binding.form.remux_writer]}
          testId="library-writer-choice"
        />
        <FolderSetting
          binding={binding}
          name="watched_folder"
          label="Watched folder"
          placeholder="/srv/media/movies-4k"
        />
        <FolderSetting
          binding={binding}
          name="output_folder"
          label="Output folder"
          placeholder="/srv/media/movies-4k-out"
        />
        <FolderSetting
          binding={binding}
          name="work_folder"
          label="Work folder"
          width="wide"
          hint="Leave empty to use Weir's private temporary folder; put it on the same volume as the output folder so finished files move instead of copying."
        />
      </div>
    </QuietFieldGroup>
  );
}

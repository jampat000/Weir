import { MmOnOffSwitch } from "../../components/ui/mm-on-off-switch";
import { ServerFolderPickerButton } from "../../components/ui/server-folder-picker-button";
import { originalsFolderLabel } from "../../lib/processing/library-mode-api";
import { examplePath } from "../../lib/ui/platform";

/** #735: the switch and folder field for keeping a clean's pre-clean original instead of deleting it. */
export function KeepOriginalSettings({
  libraryId,
  enabled,
  originalsFolder,
  editable,
  onChangeEnabled,
  onChangeFolder,
}: {
  libraryId: number;
  enabled: boolean;
  originalsFolder: string;
  editable: boolean;
  onChangeEnabled: (next: boolean) => void;
  onChangeFolder: (folder: string) => void;
}) {
  return (
    <>
      <MmOnOffSwitch
        id={`library-${libraryId}-keep-original`}
        label={`Files already in your library: keep the original after cleaning (move it to ${originalsFolderLabel(originalsFolder)})`}
        enabled={enabled}
        disabled={!editable}
        onChange={onChangeEnabled}
      />
      {enabled ? (
        <div className="mm-library-setup__originals-folder">
          <p className="mm-library-setup__hint">
            Weir moves the original into {originalsFolderLabel(originalsFolder)}{" "}
            instead of deleting it, so removed tracks can be recovered.
            You&rsquo;ll need the disk space for both.
          </p>
          <input
            className="mm-input"
            placeholder={examplePath(
              String.raw`D:\Media\Movies\.weir-originals`,
              "/media/movies/.weir-originals",
            )}
            aria-label="Originals folder"
            value={originalsFolder}
            disabled={!editable}
            onChange={(event) => onChangeFolder(event.target.value)}
          />
          <ServerFolderPickerButton
            title="Choose where kept originals go"
            value={originalsFolder}
            onSelect={onChangeFolder}
          />
          <p className="mm-library-setup__hint">
            Inside a library folder, this folder&rsquo;s name must start with a
            dot (like .weir-originals) so your media manager doesn&rsquo;t
            import the originals; outside a library folder, any name works.
          </p>
        </div>
      ) : null}
    </>
  );
}

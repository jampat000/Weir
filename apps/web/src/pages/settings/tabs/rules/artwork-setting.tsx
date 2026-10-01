import { useState } from "react";

import { SettingRow } from "../../../../components/shared/settings-group";
import { MmOnOffSwitch } from "../../../../components/ui/mm-on-off-switch";
import { errorMessage } from "../../../../lib/api/error-message";
import type { ProcessingMetadataProvider } from "../../../../lib/processing/metadata-provider-api";
import { useSaveProcessingMetadataProvider } from "../../../../lib/processing/metadata-provider-queries";

const SWITCH_ID = "artwork-setting";

/**
 * Whether Weir looks up posters. It saves the moment it is changed, and says only what it sends away: the posters
 * come through Deluno's metadata service, which needs no key.
 */
export function ArtworkSetting({
  saved,
  editable,
}: {
  saved: ProcessingMetadataProvider | undefined;
  editable: boolean;
}) {
  const save = useSaveProcessingMetadataProvider();
  const [problem, setProblem] = useState<string | null>(null);

  const change = (artworkEnabled: boolean) => {
    if (!saved) return;
    setProblem(null);
    save.mutate(
      {
        provider: saved.provider === "tmdb" ? "tmdb" : "",
        base_url: saved.base_url,
        artwork_enabled: artworkEnabled,
      },
      {
        onError: (error) =>
          setProblem(
            errorMessage(error, "The artwork setting could not be saved."),
          ),
      },
    );
  };

  return (
    <>
      <SettingRow
        label="Artwork"
        hint="Weir looks up each title's poster through Deluno's metadata service. Only the title and year are sent."
      >
        <MmOnOffSwitch
          id={SWITCH_ID}
          label="Artwork"
          enabled={saved?.artwork_enabled ?? true}
          disabled={!editable || !saved || save.isPending}
          layout="control"
          onChange={change}
        />
      </SettingRow>
      {problem ? (
        <p role="alert" className="mm-status-text--failed mt-2 text-sm">
          {problem}
        </p>
      ) : null}
    </>
  );
}

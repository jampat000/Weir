import { PageLoading } from "../../../../components/shared/page-loading";
import { QuietSection } from "../../../../components/shared/quiet-section";
import { canEdit } from "../../../../lib/auth/can-edit";
import { useMeQuery } from "../../../../lib/auth/queries";
import { useProcessingMetadataProviderQuery } from "../../../../lib/processing/metadata-provider-queries";
import { SaveModelNote } from "../../save-model-note";
import { SettingsLoadError } from "../../settings-load-error";
import { ArtworkSetting } from "./artwork-setting";
import {
  MetadataProviderSection,
  useProviderDraft,
} from "./metadata-provider-section";

/**
 * Setup › Rules › Metadata & artwork: the provider that says which language a title was made in, and whether
 * Weir looks up posters. The provider saves on its Save; the artwork switch saves as it is changed.
 */
export function MetadataTab() {
  const me = useMeQuery();
  const provider = useProcessingMetadataProviderQuery();
  const providerDraft = useProviderDraft(provider.data);
  const editable = canEdit(me.data?.role);

  if (provider.isPending || me.isPending) {
    return <PageLoading label="Loading metadata settings" />;
  }
  if (provider.isError) return <SettingsLoadError what="metadata settings" />;

  return (
    <div className="mm-quiet-stack" data-testid="processing-metadata-tab">
      <MetadataProviderSection
        saved={provider.data}
        draft={providerDraft}
        editable={editable}
      />
      <QuietSection headingId="processing-artwork-heading" heading="Artwork">
        <div className="mm-quiet-stack">
          <SaveModelNote model="instant" />
          <ArtworkSetting saved={provider.data} editable={editable} />
        </div>
      </QuietSection>
    </div>
  );
}

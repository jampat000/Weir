import { RulesPreviewPanel } from "./rules-preview-panel";
import {
  ContainerFold,
  OriginalLanguageFold,
  TrackNamingFold,
} from "./rule-folds";
import type { RuleSetBinding } from "./rule-set-fields";
import { TrackOrderSection } from "./track-order-section";
import { AudioRulesGroup, SubtitleRulesGroup } from "./track-rules-groups";

/**
 * Every rule of one profile. The live example comes first, so a file's result is on screen while the
 * rules below change it; the numbered groups sit side by side where there is room.
 */
export function ProfileRules({
  binding,
  providerName,
  editable,
  resetKey,
}: {
  binding: RuleSetBinding;
  providerName: string;
  editable: boolean;
  /** Changes with the profile, so the ordering editor closes again on a switch. */
  resetKey: string;
}) {
  return (
    <>
      <RulesPreviewPanel rules={binding.draft} disabled={!editable} />
      <div className="mm-profile-groups">
        <AudioRulesGroup binding={binding} />
        <SubtitleRulesGroup binding={binding} />
      </div>
      <OriginalLanguageFold binding={binding} providerName={providerName} />
      <ContainerFold binding={binding} />
      <TrackNamingFold binding={binding} />
      <TrackOrderSection key={resetKey} binding={binding} />
    </>
  );
}

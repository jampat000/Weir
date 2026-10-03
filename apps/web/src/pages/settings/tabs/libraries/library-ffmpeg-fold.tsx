import { QuietDisclosure } from "../../../../components/shared/quiet-section";
import { STRICTNESS_OPTIONS } from "./library-options";
import { SelectSetting, type LibraryFormBinding } from "./library-settings";

/**
 * FFmpeg's strictness, folded away: an advanced option most workflows never touch. It applies to new
 * downloads and to cleaning files already in the library.
 */
export function LibraryFfmpegFold({
  binding,
}: {
  binding: LibraryFormBinding;
}) {
  return (
    <QuietDisclosure title="Advanced">
      <div className="mm-editor-grid">
        <SelectSetting
          binding={binding}
          name="ffmpeg_strictness"
          label="FFmpeg compatibility"
          options={STRICTNESS_OPTIONS}
          hint="How strictly FFmpeg treats unusual files. It applies to new downloads and to cleaning files already in your library."
          testId="library-ffmpeg-compatibility"
        />
      </div>
    </QuietDisclosure>
  );
}

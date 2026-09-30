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
    <details>
      <summary className="mm-library-fold__summary">
        FFmpeg compatibility (advanced)
      </summary>
      <div className="mm-field-row mt-4">
        <SelectSetting
          binding={binding}
          name="ffmpeg_strictness"
          label="FFmpeg compatibility"
          options={STRICTNESS_OPTIONS}
          hint="How strictly FFmpeg treats unusual files. It applies to new downloads and to cleaning files already in your library."
          testId="library-ffmpeg-compatibility"
        />
      </div>
    </details>
  );
}

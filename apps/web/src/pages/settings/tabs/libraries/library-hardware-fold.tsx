import { HARDWARE_DECODE_OPTIONS, STRICTNESS_OPTIONS } from "./library-options";
import {
  SelectSetting,
  TextSetting,
  type LibraryFormBinding,
} from "./library-settings";

/**
 * FFmpeg's strictness, and hardware decoding, folded away. Weir copies the video and audio without
 * decoding them, so a hardware decoder has nothing to do yet: those settings are shown as saved and
 * labelled as unavailable rather than offered as if they worked.
 */
export function LibraryHardwareFold({
  binding,
}: {
  binding: LibraryFormBinding;
}) {
  const unavailable = { ...binding, editable: false };
  return (
    <details>
      <summary className="mm-library-fold__summary">
        Hardware and compatibility
      </summary>
      <div className="mm-field-row mt-4">
        <SelectSetting
          binding={binding}
          name="ffmpeg_strictness"
          label="FFmpeg compatibility"
          options={STRICTNESS_OPTIONS}
        />
      </div>
      <p
        className="mm-library-fold__detail"
        data-testid="library-hardware-unavailable"
      >
        <span className="mm-quiet-badge mm-quiet-badge--off">
          Not available in this version
        </span>{" "}
        Hardware decoding. Weir copies the video without decoding it, so a
        hardware decoder would have nothing to do.
      </p>
      <div className="mm-field-row mt-4">
        <SelectSetting
          binding={unavailable}
          name="hardware_decode_mode"
          label="Hardware decoding"
          options={HARDWARE_DECODE_OPTIONS}
        />
        <TextSetting
          binding={unavailable}
          name="hardware_device"
          label="Hardware method"
          width="medium"
          placeholder="cuda, qsv, vaapi"
        />
        <TextSetting
          binding={unavailable}
          name="hardware_disabled_vendors_csv"
          label="Never use these vendors"
          width="medium"
          placeholder="nvidia,intel"
        />
      </div>
    </details>
  );
}

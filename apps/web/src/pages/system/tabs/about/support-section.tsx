import {
  SHOW_SUPPORT_URL_PLACEHOLDER,
  SUPPORT_URL,
} from "../../../../lib/support";
import { QuietDisclosure } from "../../../../components/shared/quiet-section";

/** Supporting Weir: optional, and nothing here changes how it works, so it stays closed. */
export function SupportSection() {
  return (
    <div data-testid="suite-settings-support-tab" className="mm-quiet-stack">
      <QuietDisclosure
        title="Support Weir"
        summaryWhenClosed="Optional"
        data-testid="suite-settings-support"
      >
        <p className="mm-quiet-note">
          Weir is free to use. Support is optional.
        </p>
        <p className="mm-quiet-note mt-2">
          If Weir saves you time or keeps your downloads clean, you can support
          ongoing development.
        </p>
        {SUPPORT_URL ? (
          <p className="mt-4">
            <a
              href={SUPPORT_URL}
              target="_blank"
              rel="noreferrer"
              className="mm-quiet-link"
              data-testid="suite-settings-support-button"
            >
              Support Weir →
            </a>
          </p>
        ) : null}
        {SHOW_SUPPORT_URL_PLACEHOLDER ? (
          <p className="mm-caption-note mt-3">
            Development note: set <code>VITE_SUPPORT_URL</code> to show the
            support button.
          </p>
        ) : null}
      </QuietDisclosure>
    </div>
  );
}

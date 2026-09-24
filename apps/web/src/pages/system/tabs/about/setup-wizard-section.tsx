import { useNavigate } from "react-router-dom";

import {
  QuietDisclosure,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import type { AppSettings } from "../../../../lib/settings/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

const WIZARD_STATE_TEXT: Record<string, string> = {
  completed: "You finished the wizard.",
  skipped: "You skipped the wizard.",
};

/** The setup wizard, to run again. Folded away: most people open it once, if ever. */
export function SetupWizardSection({ settings }: { settings: AppSettings }) {
  const navigate = useNavigate();
  const wizardState = (settings.setup_wizard_state || "pending")
    .trim()
    .toLowerCase();

  return (
    <div data-testid="suite-settings-global" className="mm-quiet-stack">
      <QuietDisclosure title="Setup wizard" summaryWhenClosed="Run once">
        <p className="mm-quiet-note">
          Go through the first-run steps again: time zone, backups and library
          folders. You can leave at any point.
        </p>
        <p className="mm-caption-note mt-3 block">
          {WIZARD_STATE_TEXT[wizardState] ??
            "The wizard has not been finished yet."}
        </p>
        <div className={`${quietActionRowClass} mt-6`}>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            data-testid="suite-settings-open-setup-wizard"
            onClick={() => navigate("/setup-wizard")}
          >
            Open setup wizard
          </button>
        </div>
      </QuietDisclosure>
    </div>
  );
}

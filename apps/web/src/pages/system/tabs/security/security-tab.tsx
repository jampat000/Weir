import { FactTable } from "../../../../components/shared/fact-table";
import { QuietSection } from "../../../../components/shared/quiet-section";
import { useCurrentSessionQuery } from "../../../../lib/auth/queries";
import { useSecurityOverviewQuery } from "../../../../lib/settings/queries";
import { ActiveSessionsSection } from "./active-sessions-section";
import { ChangePasswordSection } from "./change-password-section";
import { ChangeUsernameSection } from "./change-username-section";
import { currentSignInFacts } from "./security-facts";
import { SignInProtectionSection } from "./sign-in-protection-section";

/** System › Security: this sign-in, every sign-in, the account, and how sign-in is protected. */
export function SecurityTab() {
  const currentSessionQ = useCurrentSessionQuery();
  const overviewQ = useSecurityOverviewQuery();

  return (
    <div
      className="mm-quiet-stack w-full"
      data-testid="suite-settings-security"
    >
      <QuietSection
        level={3}
        headingId="suite-security-current-sign-in-heading"
        heading="Current sign-in"
      >
        <FactTable
          caption="How this browser is signed in"
          facts={currentSignInFacts(
            currentSessionQ.data,
            overviewQ.data,
            currentSessionQ.isError,
          )}
        />
      </QuietSection>
      <ActiveSessionsSection enabled={currentSessionQ.data !== null} />
      {/* Two short forms side by side rather than one long column. */}
      <div className="mm-security-pair">
        <ChangeUsernameSection />
        <ChangePasswordSection />
      </div>
      <SignInProtectionSection overviewQ={overviewQ} />
    </div>
  );
}

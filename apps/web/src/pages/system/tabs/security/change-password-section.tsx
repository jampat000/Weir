import { useState } from "react";
import { useNavigate } from "react-router-dom";

import { QuietSection } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import { useChangePasswordMutation } from "../../../../lib/auth/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { RevealablePasswordField } from "./security-field";

const EMPTY = { current: "", next: "", confirm: "" };

/** Changing the password ends this sign-in: Weir asks for a fresh one with the new password. */
export function ChangePasswordSection() {
  const navigate = useNavigate();
  const changePassword = useChangePasswordMutation();
  const [fields, setFields] = useState(EMPTY);
  const [status, setStatus] = useState<string | null>(null);
  // Bumped on each submit, so the fields remount with their text hidden again.
  const [attempt, setAttempt] = useState(0);
  const busy = changePassword.isPending;
  const set = (field: keyof typeof EMPTY) => (value: string) =>
    setFields((prev) => ({ ...prev, [field]: value }));

  const submit = () => {
    setStatus(null);
    if (fields.next !== fields.confirm) {
      setStatus("New passwords do not match.");
      return;
    }
    changePassword.mutate(
      { currentPassword: fields.current, newPassword: fields.next },
      {
        onSuccess: () => {
          setFields(EMPTY);
          setStatus("Password changed. Sign in again with your new password.");
          void navigate("/login", { replace: true });
        },
        onSettled: () => setAttempt((n) => n + 1),
      },
    );
  };

  return (
    <QuietSection
      level={3}
      headingId="suite-security-change-password-heading"
      heading="Change password"
    >
      <p className="mm-quiet-note">
        Update your sign-in password. After saving, Weir requires a fresh
        sign-in.
      </p>
      <div className="mt-4 max-w-xl space-y-3" key={attempt}>
        <RevealablePasswordField
          label="Current password"
          placeholder="Enter current password"
          autoComplete="current-password"
          value={fields.current}
          onChange={set("current")}
          disabled={busy}
        />
        <RevealablePasswordField
          label="New password (min. 8 characters)"
          placeholder="Enter new password"
          autoComplete="new-password"
          value={fields.next}
          onChange={set("next")}
          disabled={busy}
        />
        <RevealablePasswordField
          label="Confirm new password"
          placeholder="Re-enter new password"
          autoComplete="new-password"
          value={fields.confirm}
          onChange={set("confirm")}
          disabled={busy}
        />
        {changePassword.isError ? (
          <p className="mm-status-text--failed text-sm" role="alert">
            {errorMessage(changePassword.error, "Could not change password.")}
          </p>
        ) : null}
        {status ? (
          <p className="text-sm text-mm-text2" role="status">
            {status}
          </p>
        ) : null}
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={
            busy ||
            fields.current.trim() === "" ||
            fields.next.trim() === "" ||
            fields.confirm.trim() === ""
          }
          onClick={submit}
        >
          {busy ? "Saving..." : "Change password"}
        </button>
      </div>
    </QuietSection>
  );
}

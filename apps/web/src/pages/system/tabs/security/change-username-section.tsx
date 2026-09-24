import { useState } from "react";

import { QuietSection } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import { useChangeUsernameMutation } from "../../../../lib/auth/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { SecurityField } from "./security-field";

/** Weir has one account; renaming it asks for the password first. */
export function ChangeUsernameSection() {
  const changeUsername = useChangeUsernameMutation();
  const [newUsername, setNewUsername] = useState("");
  const [password, setPassword] = useState("");
  const busy = changeUsername.isPending;

  const submit = () =>
    changeUsername.mutate(
      { currentPassword: password, newUsername: newUsername.trim() },
      {
        onSuccess: () => {
          setNewUsername("");
          setPassword("");
        },
      },
    );

  return (
    <QuietSection
      level={3}
      headingId="suite-security-change-username-heading"
      heading="Change username"
    >
      <p className="mm-quiet-note">
        Weir has one account. Signing in ignores capitalisation, so{" "}
        <code>admin</code> and <code>Admin</code> are the same name.
      </p>
      <div className="mt-4 max-w-xl space-y-3">
        <SecurityField
          label="New username"
          placeholder="Enter a new username"
          autoComplete="username"
          value={newUsername}
          onChange={setNewUsername}
          disabled={busy}
        />
        <SecurityField
          type="password"
          label="Current password"
          placeholder="Confirm it is you"
          autoComplete="current-password"
          value={password}
          onChange={setPassword}
          disabled={busy}
        />
        {changeUsername.isError ? (
          <p className="mm-status-text--failed text-sm" role="alert">
            {errorMessage(
              changeUsername.error,
              "Could not change the username.",
            )}
          </p>
        ) : null}
        {changeUsername.isSuccess ? (
          <p className="text-sm text-mm-text2" role="status">
            {changeUsername.data.message}
          </p>
        ) : null}
        <button
          type="button"
          className={mmActionButtonClass({ variant: "secondary" })}
          disabled={busy || newUsername.trim() === "" || password === ""}
          onClick={submit}
        >
          {busy ? "Saving…" : "Change username"}
        </button>
      </div>
    </QuietSection>
  );
}

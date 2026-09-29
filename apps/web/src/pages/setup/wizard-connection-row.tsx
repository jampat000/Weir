import { useEffect, useRef } from "react";

import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";

/** What one connection made during setup shows, whichever kind of connection it is. */
export type ConnectionRowModel = {
  name: string;
  address: string;
  /** True when the last test succeeded, false when it failed, null when it was never tested. */
  answering: boolean | null;
  /** The reason a failed test gave. */
  detail: string | null;
  testing: boolean;
  testError: string | null;
};

/**
 * One connection: what it is, whether it answers, and how to test or remove it. A connection nobody has
 * tested yet is tested as soon as it appears, so a wrong address shows here and not later.
 */
export function WizardConnectionRow({
  connection,
  onTest,
  onRemove,
  removeDisabled,
}: {
  connection: ConnectionRowModel;
  onTest: () => void;
  onRemove: () => void;
  removeDisabled: boolean;
}) {
  const { name, address, answering, detail, testing, testError } = connection;
  const startedTest = useRef(false);
  useEffect(() => {
    if (answering === null && !startedTest.current) {
      startedTest.current = true;
      onTest();
    }
  }, [answering, onTest]);

  return (
    <div className="mm-wizard-connection" data-testid="setup-wizard-connection">
      <p className="text-sm text-mm-text1">
        <span className="font-medium">{name}</span> at{" "}
        <code className="break-all">{address}</code>
      </p>
      {testing ? (
        <p className="text-sm text-mm-text2">Testing…</p>
      ) : answering ? (
        <p className="mm-status-text--healthy text-sm font-medium">
          ✓ Connected
        </p>
      ) : answering === false ? (
        <div className="space-y-1 text-sm" role="alert">
          <p className="mm-status-text--failed font-medium">
            Weir could not connect to {name}.
          </p>
          {detail ? <p className="text-mm-text2">{detail}</p> : null}
          <p className="text-mm-text2">
            Check the address, then test again. To try different details, remove
            it and add it again, or choose &ldquo;Neither&rdquo; above and pick
            the folders yourself.
          </p>
        </div>
      ) : null}
      {testError ? (
        <p className="mm-status-text--failed text-sm" role="alert">
          {testError}
        </p>
      ) : null}
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          className={mmActionButtonClass({ variant: "secondary" })}
          disabled={testing}
          onClick={onTest}
        >
          {testing ? "Testing…" : "Test"}
        </button>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "tertiary" })}
          disabled={testing || removeDisabled}
          aria-haspopup="dialog"
          onClick={onRemove}
        >
          Remove
        </button>
      </div>
    </div>
  );
}

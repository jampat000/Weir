import { useEffect, useState } from "react";

import {
  SegmentedControl,
  type SegmentedOption,
} from "../../../../components/panels/segmented-control";
import { Chip } from "../../../../components/panels/chip";
import { LoadError } from "../../../../components/shared/load-error";
import { ConfirmDialog } from "../../../../components/ui/confirm-dialog";
import { CopyLink } from "../../../../components/ui/copy-link";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  useNetworkAccessMutation,
  useNetworkAccessQuery,
} from "../../../../lib/settings/queries";
import type { NetworkScope } from "../../../../lib/settings/types";
import { describeNetworkAccess, intendedScope } from "./network-access-state";

const SCOPE_OPTIONS: readonly SegmentedOption<NetworkScope>[] = [
  { value: "this_pc_only", label: "This PC only" },
  { value: "network", label: "Devices on my network" },
];

/** How long the page keeps reading the state after someone asks Windows again: the prompt waits for a person. */
const RETRY_WATCH_MS = 60_000;

/**
 * Who can reach Weir over the network, shown and changed on System › About's "This PC" card. The choice is saved
 * at once and carried out by the Windows tray: it asks Windows for permission on the PC itself, restarts Weir, and
 * this page follows along until it is done. Where Weir does not manage this (Docker, a bare install) it says what
 * does instead.
 */
export function NetworkAccessSetting({ editable }: { editable: boolean }) {
  const [retrying, setRetrying] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const query = useNetworkAccessQuery(retrying);
  const change = useNetworkAccessMutation();
  const status = query.data;

  useEffect(() => {
    if (!retrying) return;
    const timer = window.setTimeout(() => setRetrying(false), RETRY_WATCH_MS);
    return () => window.clearTimeout(timer);
  }, [retrying]);

  if (query.isPending) {
    return <p className="mm-sys-note">Checking…</p>;
  }
  if (!status) {
    return <LoadError thing="the network setting" error={query.error} />;
  }
  if (status.state === "not_applicable") {
    return (
      <p className="mm-sys-note" data-testid="network-access-unmanaged">
        {status.summary}
      </p>
    );
  }

  const line = describeNetworkAccess(status);
  const choose = (scope: NetworkScope) => {
    if (scope === intendedScope(status) || change.isPending) return;
    if (scope === "network") {
      change.reset();
      setConfirming(true);
      return;
    }
    change.mutate({ scope });
  };
  const retry = () => {
    setRetrying(true);
    change.mutate({ scope: "network" });
  };
  const closeConfirm = () => {
    change.reset();
    setConfirming(false);
  };

  return (
    <div className="space-y-2.5" data-testid="network-access-setting">
      {editable ? (
        <SegmentedControl
          options={SCOPE_OPTIONS}
          value={intendedScope(status)}
          onChange={choose}
          ariaLabel="Who can reach Weir"
          dataTestId="network-access-scope"
        />
      ) : null}
      <div
        className="mm-status-line flex-wrap"
        title={status.summary}
        data-testid="network-access-status"
      >
        <Chip tone={line.tone}>{line.label}</Chip>
        {line.addresses.map((address) => (
          <span key={address} className="inline-flex items-center gap-2">
            <code>{address}</code>
            <CopyLink value={address} label={address} />
          </span>
        ))}
        {line.canRetry && editable ? (
          <button
            type="button"
            className="mm-quiet-link"
            disabled={change.isPending}
            onClick={retry}
          >
            Try again
          </button>
        ) : null}
      </div>
      {retrying && line.canRetry ? (
        <p className="mm-sys-note">
          Windows may be asking for approval on {status.machine_name}.
        </p>
      ) : null}
      {line.note ? <p className="mm-sys-note">{line.note}</p> : null}
      {change.isError && !confirming ? (
        <p className="mm-status-text--failed text-sm" role="alert">
          {errorMessage(change.error, "Weir couldn't change who can reach it.")}
        </p>
      ) : null}
      {confirming ? (
        <ConfirmDialog
          testId="network-access-confirm"
          title="Let devices on your network reach Weir?"
          description={
            <p>
              Other devices on your network will be able to reach Weir&apos;s
              sign-in page.
            </p>
          }
          confirmLabel="Confirm"
          cancelLabel="Cancel"
          busy={change.isPending}
          busyLabel="Saving…"
          error={
            change.isError
              ? errorMessage(
                  change.error,
                  "Weir couldn't change who can reach it.",
                )
              : null
          }
          onCancel={closeConfirm}
          onConfirm={() =>
            change.mutate(
              { scope: "network" },
              { onSuccess: () => setConfirming(false) },
            )
          }
        />
      ) : null}
    </div>
  );
}

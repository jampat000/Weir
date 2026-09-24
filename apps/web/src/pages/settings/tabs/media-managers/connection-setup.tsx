import { useState } from "react";

import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import type { useGenerateMediaManagerWebhookSecret } from "../../../../lib/media-managers/queries";
import {
  mmActionButtonClass,
  mmTechnicalMonoSmallClass,
} from "../../../../lib/ui/mm-control-roles";

/**
 * The API returns a path, but this gets pasted into another app on another machine, so it needs the
 * host Weir is actually reachable on.
 */
function webhookUrl(connection: MediaManagerConnection): string {
  return `${window.location.origin}${connection.webhook_url_path}`;
}

function isArrApp(connection: MediaManagerConnection): boolean {
  return connection.kind === "sonarr" || connection.kind === "radarr";
}

/** Sonarr and Radarr: where the mapping lives, and the import webhook that tells Weir a file was taken (#652). */
function ArrInstructions({ name }: { name: string }) {
  return (
    <>
      <p
        className="mb-3 text-mm-text2"
        data-testid="media-manager-mapping-pointer"
      >
        {name} picks up what Weir cleans through a remote path mapping from a
        library&apos;s watched folder to its output folder. Open that library
        under Settings → Libraries: its editor shows the exact values to enter
        and checks that {name} has them.
      </p>
      <p
        className="mb-3 text-mm-text2"
        data-testid="media-manager-import-webhook"
      >
        So Weir hears when {name} imports a file: in {name}, open Settings →
        Connect, add a <strong>Webhook</strong>, tick <strong>On Import</strong>{" "}
        and <strong>On Upgrade</strong>, and use the address below with method
        POST. Add a header named <code>X-Webhook-Secret</code> holding the
        secret. History then says &ldquo;Imported by {name}&rdquo;, and Weir
        removes its own hand-back copy once {name} has taken it, only when that
        copy is still exactly as Weir wrote it.
      </p>
    </>
  );
}

/**
 * The address and secret are needed once, when wiring the other app up, so they are folded away and
 * the card answers "is it connected" at a glance. The browser's own disclosure triangle is hidden,
 * so the summary carries a Show / Hide link instead.
 */
export function ConnectionSetup({
  connection,
  secret,
  busy,
}: {
  connection: MediaManagerConnection;
  /** Owned by the card, so the card's other buttons wait while a secret is made. */
  secret: ReturnType<typeof useGenerateMediaManagerWebhookSecret>;
  busy: boolean;
}) {
  const [revealed, setRevealed] = useState<string | null>(null);
  return (
    <details
      className="group mt-4 border-t border-mm-border pt-3 text-xs text-mm-text3"
      data-testid="media-manager-setup-details"
    >
      <summary className="flex cursor-pointer list-none items-baseline justify-between gap-3 font-medium text-mm-text2 [&::-webkit-details-marker]:hidden">
        <span>How to point {connection.name} at Weir</span>
        <span className="mm-quiet-link group-open:hidden">Show →</span>
        <span className="mm-quiet-link hidden group-open:inline">Hide →</span>
      </summary>

      <div className="mt-3">
        {isArrApp(connection) ? (
          <ArrInstructions name={connection.name} />
        ) : null}
        <p className="text-mm-text2">
          In {connection.name}, send files to this address:
        </p>
        <code
          className={`mt-1 block ${mmTechnicalMonoSmallClass}`}
          data-testid="media-manager-webhook-url"
        >
          {webhookUrl(connection)}
        </code>

        <p className="mt-3 text-mm-text2">
          {connection.webhook_secret_is_set
            ? `${connection.name} must send its secret with every file. Anything without it is ignored.`
            : `There is no secret yet, so anything on your network could send files here pretending to be ${connection.name}.`}
        </p>

        {revealed ? (
          <div
            className="mt-2 rounded bg-mm-card-bg p-2"
            data-testid="media-manager-secret"
          >
            <code className={mmTechnicalMonoSmallClass}>{revealed}</code>
            <span className="mt-1 block text-mm-text3">
              Copy this into {connection.name} now — Weir will not show it
              again.
            </span>
          </div>
        ) : null}

        <button
          type="button"
          data-testid="media-manager-generate-secret"
          className={`mt-3 ${mmActionButtonClass({ variant: "secondary" })}`}
          disabled={busy}
          onClick={() =>
            secret.mutate(connection.id, {
              onSuccess: (data) => setRevealed(data.webhook_secret),
            })
          }
        >
          {connection.webhook_secret_is_set
            ? "Replace the secret"
            : "Create a secret"}
        </button>
      </div>
    </details>
  );
}

import { useState } from "react";

import { errorMessage } from "../../../../lib/api/error-message";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import { useGenerateMediaManagerWebhookSecret } from "../../../../lib/media-managers/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { RevealedSecret } from "./revealed-secret";

const SECRET_FAILURE = "The secret could not be created.";

/**
 * Right after a media manager is added, offer to create its secret at once instead of leaving that
 * to be found later inside the new card's own setup details. Declining changes nothing: "Create a
 * secret" is still there under the card, whenever it is wanted.
 */
export function NewConnectionSecretPrompt({
  connection,
  onDismiss,
}: {
  connection: MediaManagerConnection;
  onDismiss: () => void;
}) {
  const secret = useGenerateMediaManagerWebhookSecret();
  const [revealed, setRevealed] = useState<string | null>(null);

  if (revealed) {
    return (
      <div
        className="mm-quiet-note"
        data-testid="media-manager-new-secret-prompt"
      >
        <RevealedSecret managerName={connection.name} secret={revealed} />
        <button
          type="button"
          className={`mt-2 ${mmActionButtonClass({ variant: "secondary" })}`}
          onClick={onDismiss}
        >
          Done
        </button>
      </div>
    );
  }

  return (
    <div
      className="mm-quiet-note"
      data-testid="media-manager-new-secret-prompt"
    >
      <p>Create a secret for {connection.name} now?</p>
      {secret.isError ? (
        <p className="mm-status-text--failed mt-2 text-sm" role="alert">
          {errorMessage(secret.error, SECRET_FAILURE)}
        </p>
      ) : null}
      <div className="mt-2 flex flex-wrap gap-2">
        <button
          type="button"
          data-testid="media-manager-new-secret-create"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={secret.isPending}
          onClick={() =>
            secret.mutate(connection.id, {
              onSuccess: (data) => setRevealed(data.webhook_secret),
            })
          }
        >
          {secret.isPending ? "Creating…" : "Create a secret"}
        </button>
        <button
          type="button"
          data-testid="media-manager-new-secret-dismiss"
          className={mmActionButtonClass({ variant: "tertiary" })}
          disabled={secret.isPending}
          onClick={onDismiss}
        >
          Not now
        </button>
      </div>
    </div>
  );
}

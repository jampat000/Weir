import { useTestDownloadClientConnection } from "../download-clients/queries";
import { useTestMediaManagerConnection } from "../media-managers/queries";
import type { ConnectionKind } from "./connection-activity";

/**
 * Tests a media manager or download client now, the call Settings makes: the answer is stored on the connection and
 * its saved list is read again. Resolves to whether it answered; a test that could not run counts as not answering.
 */
export function useConnectionTest(): (connection: {
  kind: ConnectionKind;
  id: number;
}) => Promise<boolean> {
  const testManager = useTestMediaManagerConnection();
  const testClient = useTestDownloadClientConnection();
  return (connection) => {
    const test =
      connection.kind === "media_manager"
        ? testManager.mutateAsync(connection.id)
        : testClient.mutateAsync(connection.id);
    return test.then(
      (result) => result.ok,
      () => false,
    );
  };
}

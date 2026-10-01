/** Every route the simulation answers by hand. Anything else falls through to the contract's own empty answer. */
import { registerActivityRoutes } from "./routes-activity.mjs";
import { registerAuthRoutes } from "./routes-auth.mjs";
import { registerConnectionRoutes } from "./routes-connections.mjs";
import { registerFileRoutes } from "./routes-files.mjs";
import { registerLibraryPageRoutes } from "./routes-library-page.mjs";
import { registerPauseRoutes } from "./routes-pause.mjs";
import { registerSettingsRoutes } from "./routes-settings.mjs";
import { registerSystemRoutes } from "./routes-system.mjs";
import { registerWorkRoutes } from "./routes-work.mjs";
import { registerWorkflowRoutes } from "./routes-workflows.mjs";
import { Router } from "./router.mjs";

export function buildRouter() {
  const router = new Router();
  registerAuthRoutes(router);
  registerSystemRoutes(router);
  registerPauseRoutes(router);
  registerFileRoutes(router);
  registerWorkRoutes(router);
  registerActivityRoutes(router);
  registerLibraryPageRoutes(router);
  registerWorkflowRoutes(router);
  registerConnectionRoutes(router);
  registerSettingsRoutes(router);
  return router;
}

import { Navigate, Outlet, useLocation } from "react-router-dom";
import { PageLoading } from "../components/shared/page-loading";
import { useAppSettingsQuery } from "../lib/settings/queries";

export function RequireSetupWizard() {
  const location = useLocation();
  const settingsQ = useAppSettingsQuery();

  if (settingsQ.isPending) {
    return <PageLoading label="Loading setup" />;
  }
  if (settingsQ.isError || !settingsQ.data) {
    return <Outlet />;
  }

  const wizardState = (settingsQ.data.setup_wizard_state || "pending")
    .trim()
    .toLowerCase();
  if (wizardState === "pending" && location.pathname !== "/setup-wizard") {
    return <Navigate to="/setup-wizard" replace />;
  }

  return <Outlet />;
}

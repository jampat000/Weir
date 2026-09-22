import {
  Navigate,
  RouterProvider,
  createBrowserRouter,
} from "react-router-dom";
import { RouteErrorScreen } from "../components/error-boundary";
import { AppShell } from "../layouts/app-shell";
import { RequireAuth } from "./require-auth";
import { RequireSetupWizard } from "./require-setup-wizard";
import { AppHydrateFallback } from "./hydrate-fallback";
import {
  LegacyActivityRedirect,
  LegacyProcessingRedirect,
} from "./legacy-redirects";

const routeErrorElement = <RouteErrorScreen />;

// Three places since 3.2: Live (/), Library and Settings (docs/exec-plans/active/live-and-library.md).
// 3.0.0 carried no redirects because nobody had installed it yet. 3.1 has been installed, so its
// two retired addresses redirect: /activity to Settings › History and logs, and each old
// Processing tab to wherever that tab lives now (legacy-redirects.tsx). Anything older than 3.1,
// such as /dashboard, still gets the Not found page.
const router = createBrowserRouter([
  {
    path: "/login",
    lazy: async () => ({
      Component: (await import("../pages/auth/login-page")).LoginPage,
    }),
    errorElement: routeErrorElement,
    HydrateFallback: AppHydrateFallback,
  },
  {
    path: "/setup",
    lazy: async () => ({
      Component: (await import("../pages/setup/setup-page")).SetupPage,
    }),
    errorElement: routeErrorElement,
    HydrateFallback: AppHydrateFallback,
  },
  {
    path: "/",
    element: <RequireAuth />,
    errorElement: routeErrorElement,
    HydrateFallback: AppHydrateFallback,
    children: [
      {
        path: "setup-wizard",
        lazy: async () => ({
          Component: (await import("../pages/setup/setup-wizard-page"))
            .SetupWizardPage,
        }),
        errorElement: routeErrorElement,
      },
      {
        element: <RequireSetupWizard />,
        errorElement: routeErrorElement,
        children: [
          {
            element: <AppShell />,
            errorElement: routeErrorElement,
            children: [
              {
                // Live: every file Weir is working on, moving as it moves. The landing screen,
                // because what Weir is doing right now is what an operator opens the app to see.
                index: true,
                lazy: async () => ({
                  Component: (
                    await import("../pages/processing/processing-page")
                  ).ProcessingPage,
                }),
                errorElement: routeErrorElement,
              },
              {
                // The files already imported, and what Weir would do to each (library mode).
                path: "library",
                lazy: async () => ({
                  Component: (await import("../pages/library/library-page"))
                    .LibraryPage,
                }),
                errorElement: routeErrorElement,
              },
              {
                path: "activity",
                element: <LegacyActivityRedirect />,
                errorElement: routeErrorElement,
              },
              {
                path: "processing",
                element: <LegacyProcessingRedirect />,
                errorElement: routeErrorElement,
              },
              {
                path: "settings",
                lazy: async () => ({
                  Component: (await import("../pages/settings/settings-page"))
                    .SettingsPage,
                }),
                errorElement: routeErrorElement,
              },
              {
                path: "*",
                lazy: async () => ({
                  Component: (await import("../pages/not-found-page"))
                    .NotFoundPage,
                }),
                errorElement: routeErrorElement,
              },
            ],
          },
        ],
      },
    ],
  },
  {
    path: "*",
    element: <Navigate to="/login" replace />,
    errorElement: routeErrorElement,
  },
]);

export function AppRouter() {
  return <RouterProvider router={router} />;
}

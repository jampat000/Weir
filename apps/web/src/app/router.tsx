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

// Four places since 3.2: Processing (/), Library, Settings and System.
// 3.0.0 carried no redirects because nobody had installed it yet. 3.1 has been installed, so its
// two retired addresses redirect: /activity to System › History and logs, and each old
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
                // Every file Weir has touched: what it was, what Weir did and what came out.
                path: "history",
                lazy: async () => ({
                  Component: (await import("../pages/history/history-page"))
                    .HistoryPage,
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
                // Weir itself: what it is running, what it keeps, and who can sign in.
                path: "system",
                lazy: async () => ({
                  Component: (await import("../pages/system/system-page"))
                    .SystemPage,
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

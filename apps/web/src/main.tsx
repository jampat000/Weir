import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@fontsource/outfit/400.css";
import "@fontsource/outfit/500.css";
import "@fontsource/outfit/600.css";
import "@fontsource/outfit/700.css";
import { AppRouter } from "./app/router";
import { AppProviders } from "./app/providers";
import { preloadAboveTheFoldFonts } from "./app/preload-fonts";
import { prefetchOnBoot } from "./app/prefetch-on-boot";
import { StartupGate } from "./app/startup-gate";
import { AppErrorScreen, ErrorBoundary } from "./components/error-boundary";
import {
  applyAppThemeToDocument,
  currentAppTheme,
  followSystemAppTheme,
} from "./lib/ui/app-theme";
import "./index.css";

applyAppThemeToDocument(currentAppTheme());
followSystemAppTheme();
preloadAboveTheFoldFonts();
// Alongside StartupGate's own /ready polling, not after it: whoever is signed in and the app's
// settings are wanted by the very first screen a person sees once Weir is ready (#719).
prefetchOnBoot();

const el = document.getElementById("root");
if (!el) {
  throw new Error("Root element #root not found");
}

createRoot(el).render(
  <StrictMode>
    <AppProviders>
      <ErrorBoundary fallback={(error) => <AppErrorScreen error={error} />}>
        <StartupGate>
          <AppRouter />
        </StartupGate>
      </ErrorBoundary>
    </AppProviders>
  </StrictMode>,
);

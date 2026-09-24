/**
 * Browser-local colour theme; not synced to the server and safe to change instantly. Until someone
 * picks a theme with the switch, Weir follows the system's light or dark setting (#697).
 */

import { useSyncExternalStore } from "react";

export const APP_THEME_STORAGE_KEY = "weir-app-theme";

export type AppTheme = "dark" | "light";

const SYSTEM_PREFERS_LIGHT = "(prefers-color-scheme: light)";

/** Fired on the window when someone picks a theme, so every switch on the page shows it. */
const THEME_CHOSEN_EVENT = "weir-app-theme-chosen";

/** The stored choice, or null when nobody has picked one. */
export function parseAppTheme(raw: string | null): AppTheme | null {
  return raw === "light" || raw === "dark" ? raw : null;
}

export function readStoredAppTheme(): AppTheme | null {
  try {
    return parseAppTheme(localStorage.getItem(APP_THEME_STORAGE_KEY));
  } catch (error) {
    // Storage blocked (private mode, site data off): nobody's choice can have been kept.
    if (error instanceof DOMException) return null;
    throw error;
  }
}

function systemPrefersLight(): MediaQueryList | null {
  return typeof window.matchMedia === "function"
    ? window.matchMedia(SYSTEM_PREFERS_LIGHT)
    : null;
}

/** The theme in force: the one someone picked, otherwise the system's. */
export function currentAppTheme(): AppTheme {
  return (
    readStoredAppTheme() ?? (systemPrefersLight()?.matches ? "light" : "dark")
  );
}

export function applyAppThemeToDocument(theme: AppTheme): void {
  document.documentElement.setAttribute("data-mm-theme", theme);
}

export function persistAppTheme(theme: AppTheme): void {
  try {
    localStorage.setItem(APP_THEME_STORAGE_KEY, theme);
  } catch (error) {
    // Storage blocked or full: the theme still changes, for this visit only.
    if (!(error instanceof DOMException)) throw error;
  }
  applyAppThemeToDocument(theme);
  window.dispatchEvent(new Event(THEME_CHOSEN_EVENT));
}

function subscribeToAppTheme(onChange: () => void): () => void {
  const system = systemPrefersLight();
  system?.addEventListener("change", onChange);
  window.addEventListener(THEME_CHOSEN_EVENT, onChange);
  return () => {
    system?.removeEventListener("change", onChange);
    window.removeEventListener(THEME_CHOSEN_EVENT, onChange);
  };
}

/** Keeps the page on the system's theme as it changes, for as long as nobody has picked one. */
export function followSystemAppTheme(): () => void {
  return subscribeToAppTheme(() => applyAppThemeToDocument(currentAppTheme()));
}

/** The theme in force, kept current as the system setting or someone's choice changes it. */
export function useAppTheme(): AppTheme {
  return useSyncExternalStore(
    subscribeToAppTheme,
    currentAppTheme,
    (): AppTheme => "dark",
  );
}

/// <reference types="vite/client" />

// Vite's own asset-type declarations don't cover fonts; needed to preload specific font files
// (see app/preload-fonts.ts) by their built, hashed URL rather than duplicating it by hand (#719).
declare module "*.woff2?url" {
  const url: string;
  export default url;
}

interface ImportMetaEnv {
  /** Optional absolute origin for the API in production. In development the Vite `/api` proxy is used. */
  readonly VITE_API_BASE_URL?: string;
  /** Optional support/sponsorship URL used by the in-app Support Weir card. */
  readonly VITE_SUPPORT_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

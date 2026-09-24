/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Optional absolute origin for the API in production. In development the Vite `/api` proxy is used. */
  readonly VITE_API_BASE_URL?: string;
  /** Optional support/sponsorship URL used by the in-app Support Weir card. */
  readonly VITE_SUPPORT_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

import { sendJson } from "../api/send-json";
import { apiFetch, readJson, requireOk } from "../api/client";
import type { RequestBody, Schema } from "../api/types";

/** Kept by hand: the server always sends known_providers, which the schema marks optional. */
export interface ProcessingMetadataProvider {
  provider: string;
  base_url: string;
  key_configured: boolean;
  known_providers: string[];
}

export type ProcessingMetadataProviderWrite = RequestBody<"MetadataProviderIn">;
export type ProcessingMetadataProviderTest = Schema<"MetadataProviderTestOut">;

const path = "/api/v1/processing/metadata-provider";

export async function fetchProcessingMetadataProvider(): Promise<ProcessingMetadataProvider> {
  const response = await apiFetch(path);
  await requireOk(path, response, "Could not load the metadata provider");
  return readJson<ProcessingMetadataProvider>(response);
}

export async function putProcessingMetadataProvider(
  data: ProcessingMetadataProviderWrite,
): Promise<ProcessingMetadataProvider> {
  const response = await sendJson(
    path,
    "PUT",
    data,
    "Could not save the metadata provider",
  );
  return readJson<ProcessingMetadataProvider>(response);
}

export async function testProcessingMetadataProvider(
  data: ProcessingMetadataProviderWrite,
): Promise<ProcessingMetadataProviderTest> {
  const response = await sendJson(
    `${path}/test`,
    "POST",
    data,
    "Could not test the metadata provider",
  );
  return readJson<ProcessingMetadataProviderTest>(response);
}

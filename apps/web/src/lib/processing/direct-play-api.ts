import { sendJson } from "../api/send-json";
import { apiFetch, readJson, requireOk } from "../api/client";
import type { Schema } from "../api/types";

/** A device the Direct Play badge can answer for. */
export type DirectPlayDevice = Schema<"DirectPlayDeviceOut">;
export type DirectPlayDevices = Schema<"DirectPlayDevicesOut">;

export const directPlayDevicesPath = () =>
  "/api/v1/processing/direct-play/devices";

export async function fetchDirectPlayDevices(): Promise<DirectPlayDevices> {
  const path = directPlayDevicesPath();
  const r = await apiFetch(path);
  await requireOk(path, r, "Could not load the Direct Play devices");
  return readJson<DirectPlayDevices>(r);
}

export async function putDirectPlayDevices(
  selected: string[],
): Promise<DirectPlayDevices> {
  const path = directPlayDevicesPath();
  const r = await sendJson(
    path,
    "PUT",
    { selected },
    "Could not save your Direct Play devices",
  );
  return readJson<DirectPlayDevices>(r);
}

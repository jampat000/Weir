import { useEffect, useState } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import {
  QuietSection,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { canEdit } from "../../../../lib/auth/can-edit";
import { useMeQuery } from "../../../../lib/auth/queries";
import type { DirectPlayDevice } from "../../../../lib/processing/direct-play-api";
import {
  useDirectPlayDevicesQuery,
  useDirectPlayDevicesSaveMutation,
} from "../../../../lib/processing/direct-play-queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { errorMessage } from "../../../../lib/api/error-message";
import { SaveModelNote } from "../../save-model-note";
import { SettingsLoadError } from "../../settings-load-error";

/** Splits "https://… (checked 2026-08-13)" into its link and its date. */
function parseSource(source: string): {
  url: string | null;
  checked: string | null;
} {
  const url = source.match(/https?:\/\/\S+/)?.[0] ?? null;
  const checked = source.match(/checked\s+(\d{4}-\d{2}-\d{2})/i)?.[1] ?? null;
  return { url, checked };
}

function DeviceSource({
  device,
}: {
  device: DirectPlayDevice;
}): React.ReactElement {
  const { url, checked } = parseSource(device.source);
  if (!url) {
    return <span>Source: {device.source}</span>;
  }
  return (
    <span>
      <a
        className="underline-offset-2 hover:underline"
        href={url}
        target="_blank"
        rel="noopener noreferrer"
        aria-label={`Source for ${device.name}`}
      >
        Source
      </a>
      {checked ? ` · checked ${checked}` : null}
    </span>
  );
}

/**
 * Which devices the operator owns, so each file can say whether it will play on them without
 * the media server converting it (#467). Information only: it changes the badge on files and
 * nothing about how a file is processed. It is also the list of devices a later "convert video so
 * my TV can play it directly" rule will aim at.
 */
export function PlaybackDevicesSection() {
  const me = useMeQuery();
  const q = useDirectPlayDevicesQuery();
  const save = useDirectPlayDevicesSaveMutation();
  const editable = canEdit(me.data?.role);
  const [selected, setSelected] = useState<Set<string>>(() => new Set());

  useEffect(() => {
    if (!q.data) return;
    setSelected(
      new Set(q.data.devices.filter((d) => d.selected).map((d) => d.id)),
    );
  }, [q.data]);

  if (q.isPending || me.isPending) {
    return <PageLoading label="Loading your playback devices" />;
  }
  if (q.isError) {
    return <SettingsLoadError what="playback devices" />;
  }
  if (!q.data) return null;

  const devices = q.data.devices;
  const dirty = devices.some((d) => d.selected !== selected.has(d.id));
  const canSave = editable && dirty && !save.isPending;

  const toggle = (id: string) => {
    setSelected((previous) => {
      const next = new Set(previous);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  return (
    <QuietSection
      headingId="processing-direct-play-heading"
      heading="Your playback devices"
      data-testid="processing-direct-play-section"
    >
      <div className="mm-quiet-stack">
        <SaveModelNote model="explicit" />
        <p className="mm-quiet-note">
          Tick the devices you watch on. Each file then shows whether it can
          Direct Play on them, meaning your media server can play it as it is,
          without converting it. Information only: Weir never changes a file
          because of this.
        </p>
      </div>
      {q.data.customised ? (
        <p
          className="mm-quiet-note mt-2"
          data-testid="processing-direct-play-customised"
        >
          This list comes from your own direct-play-devices.json in the Weir
          data folder.
        </p>
      ) : null}
      {!editable ? (
        <p className="mm-quiet-note mt-2">
          Only an operator or admin can change which devices are chosen.
        </p>
      ) : null}
      <div className="mt-6 text-sm leading-relaxed text-mm-text2">
        {devices.length === 0 ? (
          <p className="text-mm-text3">No devices are listed.</p>
        ) : (
          <ul className="grid gap-x-10 lg:grid-cols-2">
            {devices.map((device) => (
              <li
                key={device.id}
                className="flex items-start gap-3 border-b border-mm-border py-3"
              >
                <input
                  id={`direct-play-device-${device.id}`}
                  type="checkbox"
                  className="mt-1 h-4 w-4 shrink-0 accent-mm-accent"
                  checked={selected.has(device.id)}
                  disabled={!editable || save.isPending}
                  onChange={() => toggle(device.id)}
                />
                <div className="min-w-0">
                  <label
                    htmlFor={`direct-play-device-${device.id}`}
                    className="block font-medium text-mm-text1"
                  >
                    {device.name}
                  </label>
                  <p className="mt-0.5 text-xs leading-5 text-mm-text3">
                    {device.note}
                  </p>
                  <p className="mt-0.5 text-xs leading-5 text-mm-text3 [overflow-wrap:anywhere]">
                    <DeviceSource device={device} />
                  </p>
                </div>
              </li>
            ))}
          </ul>
        )}
        {save.isError ? (
          <p className="mm-status-text--failed mt-3 text-sm" role="alert">
            {errorMessage(save.error, "Save failed.")}
          </p>
        ) : null}
      </div>
      <div className={`${quietActionRowClass} mt-8`}>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={!canSave}
          onClick={() =>
            save.mutate(
              devices.filter((d) => selected.has(d.id)).map((d) => d.id),
            )
          }
        >
          {save.isPending ? "Saving…" : "Save devices"}
        </button>
      </div>
    </QuietSection>
  );
}

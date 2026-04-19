import { useEffect, useState, type ReactNode } from "react";
import { fetchCsrfToken } from "../../lib/api/auth-api";
import { MmOnOffSwitch } from "../../components/ui/mm-on-off-switch";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import type { SubberProviderOut, SubberProviderPutIn } from "../../lib/subber/subber-api";
import {
  usePutSubberProviderMutation,
  usePutSubberSettingsMutation,
  useSubberProvidersQuery,
  useSubberSettingsQuery,
  useSubberTestOpensubtitlesMutation,
  useSubberTestProviderMutation,
} from "../../lib/subber/subber-queries";

const MASK = "\u2022".repeat(10);

type ConnectionOutcome = null | "ok" | "fail";

type ConnectionCheckState = {
  outcome: ConnectionOutcome;
  at: string | null;
  detail: string;
  quotaNote?: string;
};

const initialCheck: ConnectionCheckState = { outcome: null, at: null, detail: "" };

function providerNeedsConfigureButton(p: SubberProviderOut): boolean {
  if (p.provider_key === "subscene") return false;
  if (p.requires_account) return true;
  if (p.provider_key === "podnapisi") return true;
  return false;
}

function formatLastCheck(iso: string | null): string {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

function parseOpensubtitlesQuota(message: string): { rest: string; quota?: string } {
  const lower = message.toLowerCase();
  const key = "remaining quota:";
  const idx = lower.indexOf(key);
  if (idx === -1) return { rest: message };
  const quotaPart = message.slice(idx + key.length).trim().replace(/\.\s*$/, "");
  const rest = message.slice(0, idx).trim() || "Connected.";
  return { rest, quota: `Remaining quota: ${quotaPart}` };
}

function ConnectionStatusPanel({
  check,
  idleHelper,
}: {
  check: ConnectionCheckState;
  idleHelper?: string;
}) {
  const main =
    check.outcome === null ? "Not connected yet" : check.outcome === "ok" ? "Connected" : "Connection failed";
  return (
    <div className="mt-4 rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)] p-3.5 text-sm text-[var(--mm-text2)]">
      <p className="text-sm font-medium text-[var(--mm-text)]">{main}</p>
      <p className="mt-1 text-xs text-[var(--mm-text2)]">
        Last completed check: <span className="font-medium text-[var(--mm-text)]">{formatLastCheck(check.at)}</span>
      </p>
      {check.outcome === "ok" && check.quotaNote ? <p className="mt-1 text-xs text-[var(--mm-text2)]">{check.quotaNote}</p> : null}
      {check.outcome === "ok" && check.detail && !check.quotaNote ? (
        <p className="mt-1 text-xs text-[var(--mm-text2)]">{check.detail}</p>
      ) : null}
      {check.outcome === "fail" && check.detail ? <p className="mt-1 text-xs text-red-400">{check.detail}</p> : null}
      {check.outcome === null && idleHelper ? <p className="mt-2 text-xs text-[var(--mm-text2)]">{idleHelper}</p> : null}
    </div>
  );
}

function SaveFeedback({ ok, err }: { ok: boolean; err: string | null }) {
  if (err) {
    return (
      <p className="mt-2 text-sm text-red-400" role="alert">
        {err}
      </p>
    );
  }
  if (ok) {
    return (
      <p className="mt-2 text-sm text-emerald-600" role="status">
        Saved.
      </p>
    );
  }
  return null;
}

function SubberSettingsSection({
  eyebrow,
  title,
  description,
  children,
  "data-testid": dataTestId,
}: {
  eyebrow?: string;
  title: string;
  description?: ReactNode;
  children: ReactNode;
  "data-testid"?: string;
}) {
  return (
    <section
      className="overflow-hidden rounded-lg border border-[var(--mm-border)] bg-[var(--mm-card-bg)] shadow-sm"
      data-testid={dataTestId}
    >
      <header className="border-b border-[var(--mm-border)] bg-black/10 px-5 py-4">
        {eyebrow ? (
          <p className="text-[0.7rem] font-semibold uppercase tracking-[0.12em] text-[var(--mm-text2)]">{eyebrow}</p>
        ) : null}
        <h2
          className={
            eyebrow
              ? "mt-1 text-lg font-semibold tracking-tight text-[var(--mm-text)]"
              : "text-lg font-semibold tracking-tight text-[var(--mm-text)]"
          }
        >
          {title}
        </h2>
        {description ? <div className="mt-2 text-sm leading-relaxed text-[var(--mm-text2)]">{description}</div> : null}
      </header>
      <div className="space-y-5 px-5 py-5">{children}</div>
    </section>
  );
}

function SubberSettingsSubsection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="rounded-md border border-[var(--mm-border)] bg-black/[0.06] p-4">
      <h3 className="mb-3 text-sm font-semibold text-[var(--mm-text)]">{title}</h3>
      <div className="space-y-3">{children}</div>
    </div>
  );
}

export function SubberProvidersTab({ canOperate }: { canOperate: boolean }) {
  const q = useSubberSettingsQuery();
  const pq = useSubberProvidersQuery();
  const put = usePutSubberSettingsMutation();
  const putProv = usePutSubberProviderMutation();
  const testOs = useSubberTestOpensubtitlesMutation();
  const testProv = useSubberTestProviderMutation();

  const [osUser, setOsUser] = useState("");
  const [osPass, setOsPass] = useState("");
  const [osKey, setOsKey] = useState("");
  const [showOsPass, setShowOsPass] = useState(false);
  const [showOsKey, setShowOsKey] = useState(false);
  const [provUser, setProvUser] = useState<Record<string, string>>({});
  const [provPass, setProvPass] = useState<Record<string, string>>({});
  const [provKey, setProvKey] = useState<Record<string, string>>({});
  const [provPri, setProvPri] = useState<Record<string, number>>({});
  const [provMsg, setProvMsg] = useState<Record<string, string | null>>({});
  const [osCheck, setOsCheck] = useState<ConnectionCheckState>(initialCheck);
  const [saveOs, setSaveOs] = useState({ ok: false, err: null as string | null });
  const [expandedProviderKey, setExpandedProviderKey] = useState<string | null>(null);

  useEffect(() => {
    const d = q.data;
    if (!d) return;
    setOsUser(d.opensubtitles_username ?? "");
  }, [q.data]);

  useEffect(() => {
    if (!pq.data) return;
    setProvPri((prev) => {
      const n = { ...prev };
      for (const p of pq.data) {
        if (n[p.provider_key] === undefined) n[p.provider_key] = p.priority;
      }
      return n;
    });
  }, [pq.data]);

  const dis = !canOperate || put.isPending;

  function flashSave(setter: typeof setSaveOs) {
    setter({ ok: true, err: null });
    window.setTimeout(() => setter({ ok: false, err: null }), 2500);
  }

  async function saveOpenSubtitles() {
    setSaveOs({ ok: false, err: null });
    try {
      const csrf_token = await fetchCsrfToken();
      const body: Parameters<typeof put.mutateAsync>[0] = {
        csrf_token,
        opensubtitles_username: osUser.trim(),
      };
      if (osPass.trim()) body.opensubtitles_password = osPass;
      if (osKey.trim()) body.opensubtitles_api_key = osKey;
      await put.mutateAsync(body);
      flashSave(setSaveOs);
    } catch (e) {
      setSaveOs({ ok: false, err: (e as Error).message });
    }
  }

  async function runTestOs() {
    const at = new Date().toISOString();
    try {
      const r = await testOs.mutateAsync();
      if (r.ok) {
        const parsed = parseOpensubtitlesQuota(r.message || "");
        setOsCheck({
          outcome: "ok",
          at,
          detail: parsed.rest,
          quotaNote: parsed.quota,
        });
      } else {
        setOsCheck({ outcome: "fail", at, detail: r.message || "Unknown error" });
      }
    } catch (e) {
      setOsCheck({ outcome: "fail", at, detail: (e as Error).message });
    }
  }

  async function saveProviderRow(pk: string, enabledP: boolean, priority: number) {
    const csrf_token = await fetchCsrfToken();
    const body: SubberProviderPutIn = { csrf_token, enabled: enabledP, priority };
    const u = provUser[pk]?.trim();
    const p = provPass[pk]?.trim();
    const k = provKey[pk]?.trim();
    if (u) body.username = u;
    if (p) body.password = p;
    if (k) body.api_key = k;
    await putProv.mutateAsync({ providerKey: pk, body });
  }

  async function saveExpandedProvider(pk: string, enabledP: boolean, priority: number) {
    await saveProviderRow(pk, enabledP, priority);
    setExpandedProviderKey(null);
  }

  async function runProvTest(pk: string) {
    setProvMsg((m) => ({ ...m, [pk]: null }));
    try {
      const r = await testProv.mutateAsync(pk);
      setProvMsg((m) => ({ ...m, [pk]: r.ok ? "Connected" : r.message }));
    } catch (e) {
      setProvMsg((m) => ({ ...m, [pk]: (e as Error).message }));
    }
  }

  if (q.isLoading) return <p className="text-sm text-[var(--mm-text2)]">Loading settings…</p>;
  if (q.isError) return <p className="text-sm text-red-600">{(q.error as Error).message}</p>;

  const sorted = [...(pq.data ?? [])].sort((a, b) => a.priority - b.priority || a.provider_key.localeCompare(b.provider_key));

  return (
    <div className="space-y-8" data-testid="subber-providers-tab">
      <p className="text-sm text-[var(--mm-text2)] mb-4">
        Subber searches providers in priority order until a subtitle is found. Click any provider to configure credentials and enable it.
      </p>

      <SubberSettingsSection
        eyebrow="Subtitles account"
        title="OpenSubtitles"
        description="Sign up at opensubtitles.com for a free account and API key. Subber shows your remaining download quota when you test the connection."
      >
        <SubberSettingsSubsection title="Credentials">
          <label className="block text-sm font-medium text-[var(--mm-text)]" htmlFor="subber-os-user">
            Username
          </label>
          <input
            id="subber-os-user"
            className="mm-input mt-1 w-full max-w-md"
            value={osUser}
            disabled={dis}
            onChange={(e) => setOsUser(e.target.value)}
          />
          <label className="mt-3 block text-sm font-medium text-[var(--mm-text)]" htmlFor="subber-os-pass">
            Password {q.data?.opensubtitles_password_set ? <span className="text-xs font-normal text-[var(--mm-text2)]">(leave blank to keep)</span> : null}
          </label>
          <div className="mt-1 flex max-w-md gap-2">
            <input
              id="subber-os-pass"
              className="mm-input flex-1"
              type={showOsPass ? "text" : "password"}
              value={osPass}
              placeholder={q.data?.opensubtitles_password_set ? MASK : ""}
              disabled={dis}
              onChange={(e) => setOsPass(e.target.value)}
            />
            <button type="button" className={mmActionButtonClass({ variant: "secondary" })} onClick={() => setShowOsPass(!showOsPass)}>
              {showOsPass ? "Hide" : "Show"}
            </button>
          </div>
          <label className="mt-3 block text-sm font-medium text-[var(--mm-text)]" htmlFor="subber-os-key">
            API key
          </label>
          <div className="mt-1 flex max-w-md gap-2">
            <input
              id="subber-os-key"
              className="mm-input flex-1"
              type={showOsKey ? "text" : "password"}
              value={osKey}
              placeholder={q.data?.opensubtitles_api_key_set ? MASK : ""}
              disabled={dis}
              onChange={(e) => setOsKey(e.target.value)}
            />
            <button type="button" className={mmActionButtonClass({ variant: "secondary" })} onClick={() => setShowOsKey(!showOsKey)}>
              {showOsKey ? "Hide" : "Show"}
            </button>
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            <button
              type="button"
              className={mmActionButtonClass({ variant: "primary", disabled: dis })}
              disabled={dis}
              onClick={() => void saveOpenSubtitles()}
              data-testid="subber-save-opensubtitles"
            >
              Save OpenSubtitles
            </button>
            <button
              type="button"
              className={mmActionButtonClass({ variant: "secondary", disabled: dis || testOs.isPending })}
              disabled={dis || testOs.isPending}
              onClick={() => void runTestOs()}
              data-testid="subber-test-opensubtitles"
            >
              Test connection
            </button>
          </div>
          <SaveFeedback ok={saveOs.ok} err={saveOs.err} />
          <ConnectionStatusPanel
            check={osCheck}
            idleHelper="Run a test to verify your credentials and see your remaining download quota."
          />
        </SubberSettingsSubsection>
      </SubberSettingsSection>

      <SubberSettingsSection
        eyebrow="Sources"
        title="Subtitle providers"
        description="Subber walks this list in order until a subtitle is found. Lower number = searched first. Keep at least one provider enabled."
        data-testid="subber-providers-section"
      >
        {pq.isLoading ? <p className="text-sm text-[var(--mm-text2)]">Loading providers…</p> : null}
        <ul className="divide-y divide-[var(--mm-border)] rounded-md border border-[var(--mm-border)] bg-black/10">
          {sorted.map((p) => {
            const pri = provPri[p.provider_key] ?? p.priority;
            const provMsgText = provMsg[p.provider_key];
            const showCfg = providerNeedsConfigureButton(p);
            const expanded = expandedProviderKey === p.provider_key;
            let statusEl: ReactNode;
            if (provMsgText) {
              statusEl =
                provMsgText === "Connected" ? (
                  <span className="text-xs font-medium text-emerald-600">{provMsgText}</span>
                ) : (
                  <span className="max-w-[14rem] truncate text-xs text-[var(--mm-text2)]" title={provMsgText}>
                    {provMsgText}
                  </span>
                );
            } else if (p.provider_key === "subscene") {
              statusEl = <span className="text-xs text-[var(--mm-text2)]">No account needed</span>;
            } else if (p.requires_account) {
              statusEl = p.has_credentials ? (
                <span className="text-xs font-medium text-emerald-600">Configured</span>
              ) : (
                <span className="text-xs text-[var(--mm-text2)]">Not configured</span>
              );
            } else if (p.provider_key === "podnapisi") {
              statusEl = <span className="text-xs text-[var(--mm-text2)]">Optional credentials</span>;
            } else {
              statusEl = <span className="text-xs text-[var(--mm-text2)]">—</span>;
            }
            return (
              <li key={p.provider_key} className="bg-[var(--mm-card-bg)]/40">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-2 px-3 py-2.5">
                  <span className="min-w-[7rem] text-sm font-semibold text-[var(--mm-text)]">{p.display_name}</span>
                  <div className="max-w-[11rem] shrink-0">
                    <MmOnOffSwitch
                      id={`prov-en-${p.provider_key}`}
                      label="Enabled"
                      layout="inline"
                      enabled={p.enabled}
                      disabled={dis || putProv.isPending}
                      onChange={(v) => void saveProviderRow(p.provider_key, v, pri)}
                    />
                  </div>
                  <label className="flex items-center gap-1.5 text-xs text-[var(--mm-text2)]">
                    <span className="sr-only">Priority</span>
                    <input
                      type="number"
                      className="mm-input max-w-16 text-sm"
                      disabled={dis}
                      value={pri}
                      onChange={(e) =>
                        setProvPri((x) => ({
                          ...x,
                          [p.provider_key]: Math.max(0, Math.min(9999, Number(e.target.value) || 0)),
                        }))
                      }
                      onBlur={() => {
                        if (pri !== p.priority) void saveProviderRow(p.provider_key, p.enabled, pri);
                      }}
                    />
                  </label>
                  {showCfg ? (
                    <button
                      type="button"
                      className={mmActionButtonClass({ variant: "secondary", disabled: dis })}
                      disabled={dis}
                      onClick={() => setExpandedProviderKey(expanded ? null : p.provider_key)}
                    >
                      {expanded ? "Close" : "Configure"}
                    </button>
                  ) : null}
                  <span className="ml-auto min-w-0 shrink text-right">{statusEl}</span>
                </div>
                {expanded && showCfg ? (
                  <div className="space-y-3 border-t border-[var(--mm-border)] bg-black/20 px-3 py-3">
                    {p.provider_key === "podnapisi" && !p.requires_account ? (
                      <p className="text-xs text-[var(--mm-text2)]">No account required — credentials optional.</p>
                    ) : null}
                    {p.requires_account ? (
                      <div className="space-y-2">
                        <label className="block text-xs text-[var(--mm-text2)]">
                          Username
                          <input
                            className="mm-input mt-1 w-full max-w-md"
                            disabled={dis}
                            value={provUser[p.provider_key] ?? ""}
                            onChange={(e) => setProvUser((x) => ({ ...x, [p.provider_key]: e.target.value }))}
                          />
                        </label>
                        <label className="block text-xs text-[var(--mm-text2)]">
                          Password {p.has_credentials ? <span className="text-[0.7rem]">(leave blank to keep)</span> : null}
                          <input
                            type="password"
                            className="mm-input mt-1 w-full max-w-md"
                            disabled={dis}
                            placeholder={p.has_credentials ? MASK : ""}
                            value={provPass[p.provider_key] ?? ""}
                            onChange={(e) => setProvPass((x) => ({ ...x, [p.provider_key]: e.target.value }))}
                          />
                        </label>
                        {p.provider_key.includes("opensubtitles") ? (
                          <label className="block text-xs text-[var(--mm-text2)]">
                            API key
                            <input
                              type="password"
                              className="mm-input mt-1 w-full max-w-md"
                              disabled={dis}
                              placeholder={p.has_credentials ? MASK : ""}
                              value={provKey[p.provider_key] ?? ""}
                              onChange={(e) => setProvKey((x) => ({ ...x, [p.provider_key]: e.target.value }))}
                            />
                          </label>
                        ) : null}
                      </div>
                    ) : p.provider_key === "podnapisi" ? (
                      <div className="space-y-2">
                        <label className="block text-xs text-[var(--mm-text2)]">
                          Username (optional)
                          <input
                            className="mm-input mt-1 w-full max-w-md"
                            disabled={dis}
                            value={provUser[p.provider_key] ?? ""}
                            onChange={(e) => setProvUser((x) => ({ ...x, [p.provider_key]: e.target.value }))}
                          />
                        </label>
                        <label className="block text-xs text-[var(--mm-text2)]">
                          Password (optional)
                          <input
                            type="password"
                            className="mm-input mt-1 w-full max-w-md"
                            disabled={dis}
                            placeholder={p.has_credentials ? MASK : ""}
                            value={provPass[p.provider_key] ?? ""}
                            onChange={(e) => setProvPass((x) => ({ ...x, [p.provider_key]: e.target.value }))}
                          />
                        </label>
                      </div>
                    ) : null}
                    <div className="flex flex-wrap gap-2">
                      <button
                        type="button"
                        className={mmActionButtonClass({ variant: "primary", disabled: dis || putProv.isPending })}
                        disabled={dis || putProv.isPending}
                        onClick={() => void saveExpandedProvider(p.provider_key, p.enabled, pri)}
                      >
                        Save
                      </button>
                      <button
                        type="button"
                        className={mmActionButtonClass({ variant: "secondary", disabled: dis || testProv.isPending })}
                        disabled={dis || testProv.isPending}
                        onClick={() => void runProvTest(p.provider_key)}
                      >
                        Test
                      </button>
                    </div>
                  </div>
                ) : null}
              </li>
            );
          })}
        </ul>
      </SubberSettingsSection>
    </div>
  );
}

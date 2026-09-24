import { type ReactNode, useEffect, useState } from "react";

type ReadyStep = {
  name: string;
  status: "ready" | "starting" | "failed" | string;
  detail: string;
};

type ReadyPayload = {
  ready: boolean;
  status: string;
  startup_seconds: number;
  steps: ReadyStep[];
};

type StartupState =
  | { kind: "starting"; message: string; steps: ReadyStep[]; elapsedMs: number }
  | { kind: "ready" }
  | { kind: "failed"; message: string; steps: ReadyStep[] };

const READY_PATH = "/ready";
const POLL_MS = 1000;
const STARTUP_TIMEOUT_MS = 60_000;
/** The bar starts a fifth full and creeps towards nine tenths; only ready or failed completes it. */
const ESTIMATE_START_PERCENT = 20;
const ESTIMATE_CAP_PERCENT = 90;
const ESTIMATE_MS_PER_PERCENT = 750;

const WAITING_STEP: ReadyStep = {
  name: "server",
  status: "starting",
  detail: "Waiting for Weir to start.",
};

function dotClass(step: ReadyStep, failed: boolean): string {
  if (step.status === "ready") return "mm-startup__dot mm-startup__dot--ready";
  return failed ? "mm-startup__dot mm-startup__dot--failed" : "mm-startup__dot";
}

async function fetchReadiness(signal: AbortSignal): Promise<ReadyPayload> {
  const response = await fetch(READY_PATH, {
    method: "GET",
    headers: { Accept: "application/json" },
    cache: "no-store",
    signal,
  });
  // A not-ready answer still carries the steps, so the body is read whatever the status.
  return (await response.json()) as ReadyPayload;
}

export function StartupGate({ children }: { children: ReactNode }) {
  const [state, setState] = useState<StartupState>({
    kind: "starting",
    message: "Starting Weir...",
    steps: [],
    elapsedMs: 0,
  });

  useEffect(() => {
    let cancelled = false;
    const startedAt = Date.now();
    let timer: number | undefined;

    const poll = async () => {
      const controller = new AbortController();
      try {
        const payload = await fetchReadiness(controller.signal);
        if (cancelled) {
          return;
        }
        if (payload.ready) {
          setState({ kind: "ready" });
          return;
        }
        const elapsedMs = Date.now() - startedAt;
        if (elapsedMs >= STARTUP_TIMEOUT_MS) {
          setState({
            kind: "failed",
            message:
              "Weir did not become ready in time. Check that the Weir server is still running, then refresh.",
            steps: payload.steps ?? [],
          });
          return;
        }
        setState({
          kind: "starting",
          message: "Starting Weir...",
          steps: payload.steps ?? [],
          elapsedMs,
        });
        timer = window.setTimeout(poll, POLL_MS);
      } catch {
        if (cancelled) {
          return;
        }
        const elapsedMs = Date.now() - startedAt;
        if (elapsedMs >= STARTUP_TIMEOUT_MS) {
          setState({
            kind: "failed",
            message:
              "Weir did not respond in time. Check that the Weir server is running, then refresh.",
            steps: [],
          });
          return;
        }
        setState({
          kind: "starting",
          message: "Starting Weir...",
          steps: [
            {
              name: "server",
              status: "starting",
              detail: "Waiting for the Weir server to answer.",
            },
          ],
          elapsedMs,
        });
        timer = window.setTimeout(poll, POLL_MS);
      }
    };

    void poll();
    return () => {
      cancelled = true;
      if (timer !== undefined) {
        window.clearTimeout(timer);
      }
    };
  }, []);

  if (state.kind === "ready") {
    return <>{children}</>;
  }

  return (
    <main className="mm-startup">
      <div className="mm-startup__inner">
        <p className="mm-startup__eyebrow">Weir</p>
        <h1 className="mm-startup__title">{state.message}</h1>
        <p className="mm-startup__lead">
          Preparing the local database, background workers, and schedules before
          opening the app.
        </p>
        <div className="mm-startup__card">
          {/* An estimate, not a measure: the server reports steps, not a percentage. */}
          <div
            className="mm-startup__track"
            role="progressbar"
            aria-label="Starting Weir"
          >
            <div
              className="mm-startup__fill"
              style={{
                width:
                  state.kind === "failed"
                    ? "100%"
                    : `${Math.min(ESTIMATE_CAP_PERCENT, ESTIMATE_START_PERCENT + state.elapsedMs / ESTIMATE_MS_PER_PERCENT)}%`,
              }}
            />
          </div>
          <ul className="mm-startup__steps space-y-3">
            {(state.steps.length ? state.steps : [WAITING_STEP]).map((step) => (
              <li key={step.name} className="mm-startup__step">
                <span className={dotClass(step, state.kind === "failed")} />
                <span>
                  <span className="mm-startup__step-name">{step.name}</span>
                  <span className="mm-startup__step-detail">{step.detail}</span>
                </span>
              </li>
            ))}
          </ul>
        </div>
        {state.kind === "failed" ? (
          <button
            type="button"
            className="mm-startup__retry"
            onClick={() => window.location.reload()}
          >
            Try again
          </button>
        ) : null}
      </div>
    </main>
  );
}

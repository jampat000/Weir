import {
  Component,
  type ErrorInfo,
  type ReactNode,
  useEffect,
  useRef,
} from "react";
import { isRouteErrorResponse, useRouteError } from "react-router-dom";

type ErrorBoundaryState = {
  error: Error | null;
};

type ErrorBoundaryProps = {
  children: ReactNode;
  fallback?: ReactNode | ((error: Error) => ReactNode);
};

export class ErrorBoundary extends Component<
  ErrorBoundaryProps,
  ErrorBoundaryState
> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error("Weir UI crashed", error, info.componentStack);
  }

  render(): ReactNode {
    const { error } = this.state;
    if (error) {
      const { fallback } = this.props;
      if (typeof fallback === "function") {
        return fallback(error);
      }
      return fallback ?? <AppErrorScreen error={error} />;
    }
    return this.props.children;
  }
}

/**
 * The screen shown when a route, or the whole app, has already failed. As little as possible: a
 * title, one line of plain explanation, two ways out, and the error text for a bug report. No shell
 * navigation, since rendering more of the app inside the failure is a second chance to fail.
 */
export function AppErrorScreen({
  error,
  onReload,
}: {
  error: Error;
  onReload?: () => void;
}) {
  const reload = onReload ?? (() => window.location.reload());
  const mainRef = useRef<HTMLElement>(null);

  // After a client-side route error, focus is left wherever it was, often on a control that is
  // gone, so a keyboard or screen reader user hears nothing change. Moving focus to the container
  // makes the heading the next thing read.
  useEffect(() => {
    mainRef.current?.focus();
  }, []);

  return (
    <main
      className="mm-auth-body"
      id="mm-main-content"
      ref={mainRef}
      tabIndex={-1}
    >
      <div className="mm-auth-frame">
        <section className="mm-auth-card" aria-labelledby="app-error-title">
          <h1
            className="mm-auth-title mm-auth-title--alert"
            id="app-error-title"
          >
            Something went wrong
          </h1>
          <p className="mm-auth-lead">
            Weir hit a screen error before it could finish loading this view.
            Reloading usually clears a temporary browser state problem.
          </p>
          <div className="flex flex-wrap items-center gap-4">
            <button className="mm-auth-submit" type="button" onClick={reload}>
              Reload Weir
            </button>
            {/*
              A plain anchor, not a react-router `Link`: this screen also renders from the
              `ErrorBoundary` in main.tsx, which sits outside `RouterProvider`, so a `Link`
              would throw and the error screen would itself fail to render. A full document
              load is the right escape from a crashed app in any case.
            */}
            <a
              className="text-sm font-medium text-mm-accent underline-offset-4 hover:underline"
              href="/"
            >
              Go to Processing
            </a>
          </div>
          <details className="mt-5 text-sm text-mm-text2">
            <summary className="cursor-pointer select-none rounded-mm-sm py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-mm-accent-ring">
              Show technical details
            </summary>
            <pre className="mt-2 max-h-48 overflow-auto whitespace-pre-wrap break-words rounded-mm-sm border border-mm-border bg-mm-surface-2 p-3 font-mono text-xs text-mm-text3">
              {error.message || "Unknown error"}
            </pre>
          </details>
        </section>
      </div>
    </main>
  );
}

function routeErrorToError(error: unknown): Error {
  if (error instanceof Error) {
    return error;
  }
  if (isRouteErrorResponse(error)) {
    return new Error(
      error.statusText || error.data || `Route error ${error.status}`,
    );
  }
  if (typeof error === "string" && error.trim()) {
    return new Error(error);
  }
  return new Error("Unknown route error");
}

export function RouteErrorScreen() {
  const routeError = useRouteError();
  return <AppErrorScreen error={routeErrorToError(routeError)} />;
}

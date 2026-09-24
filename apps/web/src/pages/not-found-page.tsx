import { Link } from "react-router-dom";

export function NotFoundPage() {
  return (
    <main
      className="flex min-h-[60vh] flex-col items-center justify-center px-6 py-16 text-center"
      id="mm-main-content"
      tabIndex={-1}
    >
      <p className="text-xs font-semibold uppercase tracking-[0.28em] text-mm-accent">
        404
      </p>
      <h1 className="mt-3 text-2xl font-semibold text-mm-text">
        Page not found
      </h1>
      <p className="mt-2 text-sm text-mm-text2">
        The page you&apos;re looking for doesn&apos;t exist or has moved.
      </p>
      <Link
        to="/"
        className="mt-6 text-sm font-medium text-mm-accent underline-offset-4 hover:underline"
      >
        Go to Processing
      </Link>
    </main>
  );
}

import { Link } from "react-router-dom";

/**
 * Shown inside the app shell's own `<main>` for any address Weir doesn't recognise, so no page
 * nests a second landmark and nobody has to make sense of a "404" (#697).
 */
export function NotFoundPage() {
  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center px-6 py-16 text-center">
      <h1 className="mt-3 text-2xl font-semibold text-mm-text">
        This page doesn&apos;t exist.
      </h1>
      <Link
        to="/"
        className="mt-6 text-sm font-medium text-mm-accent underline-offset-4 hover:underline"
      >
        Go to Processing
      </Link>
    </div>
  );
}

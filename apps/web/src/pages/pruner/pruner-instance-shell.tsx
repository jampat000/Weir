import { NavLink, Outlet, useParams } from "react-router-dom";
import { usePrunerInstanceQuery } from "../../lib/pruner/queries";

function tabClass(active: boolean): string {
  return [
    "rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
    active
      ? "bg-[var(--mm-surface2)] text-[var(--mm-text)]"
      : "text-[var(--mm-text2)] hover:bg-[var(--mm-surface2)] hover:text-[var(--mm-text)]",
  ].join(" ");
}

export function PrunerInstanceShell() {
  const { instanceId } = useParams();
  const id = Number(instanceId);
  const q = usePrunerInstanceQuery(id);

  if (!Number.isFinite(id) || id <= 0) {
    return (
      <div className="mm-page">
        <p className="text-sm text-red-600">Invalid instance id.</p>
      </div>
    );
  }

  const base = `/app/pruner/instances/${id}`;

  return (
    <div className="mm-page" data-testid="pruner-instance-shell">
      <header className="mm-page__header">
        <p className="mm-page__eyebrow">Pruner</p>
        {q.isLoading ? <h1 className="mm-page__title">Loading…</h1> : null}
        {q.data ? (
          <>
            <h1 className="mm-page__title">{q.data.display_name}</h1>
            <p className="mm-page__lede max-w-3xl text-[var(--mm-text2)]">
              {q.data.provider} · {q.data.base_url}
            </p>
          </>
        ) : null}
        {q.isError ? (
          <p className="mt-2 text-sm text-red-600" role="alert">
            {(q.error as Error).message}
          </p>
        ) : null}
      </header>

      <nav className="mt-4 flex flex-wrap gap-2" aria-label="Instance sections">
        <NavLink to={`${base}/overview`} className={({ isActive }) => tabClass(isActive)} end>
          Overview
        </NavLink>
        <NavLink to={`${base}/tv`} className={({ isActive }) => tabClass(isActive)}>
          TV
        </NavLink>
        <NavLink to={`${base}/movies`} className={({ isActive }) => tabClass(isActive)}>
          Movies
        </NavLink>
        <NavLink to={`${base}/connection`} className={({ isActive }) => tabClass(isActive)}>
          Connection
        </NavLink>
      </nav>

      <div className="mt-6">
        <Outlet context={{ instanceId: id, instance: q.data }} />
      </div>
    </div>
  );
}

/** Trimmer module — durable ``trimmer_jobs`` lane (see ADR-0007). */
export function TrimmerPage() {
  return (
    <div className="mm-page" data-testid="trimmer-scope-page">
      <header className="mm-page__intro">
        <p className="mm-page__eyebrow">MediaMop</p>
        <h1 className="mm-page__title">Trimmer</h1>
        <p className="mm-page__subtitle">
          Trimmer is for work that <strong>changes how much of a source you keep</strong> — cuts and length edits — using
          the Trimmer background queue. It stays separate from Fetcher download automation and from Refiner metadata
          refinement.
        </p>
      </header>

      <section
        className="mt-4 max-w-2xl space-y-3 text-sm leading-relaxed text-[var(--mm-text2)]"
        aria-labelledby="trimmer-shipped-heading"
      >
        <h2 id="trimmer-shipped-heading" className="text-base font-semibold text-[var(--mm-text)]">
          What Trimmer can run today
        </h2>
        <p data-testid="trimmer-family-trim-plan-constraints">
          <strong>Trim timing check</strong> (
          <code className="rounded bg-[var(--mm-surface2)] px-1 py-0.5 text-[0.85em] text-[var(--mm-text3)]">
            trimmer.trim_plan.constraints_check.v1
          </code>
          ) — checks start and end times you type in (seconds on a simple timeline). <strong>No</strong> video
          processing, <strong>no</strong> reading your media files from disk, and <strong>no</strong> TV or movie library
          calls — numbers only.
        </p>
        <p data-testid="trimmer-family-json-file-write">
          <strong>Trim plan file</strong> (
          <code className="rounded bg-[var(--mm-surface2)] px-1 py-0.5 text-[0.85em] text-[var(--mm-text3)]">
            trimmer.supplied_trim_plan.json_file_write.v1
          </code>
          ) — when the times are valid, saves one JSON description file under your MediaMop home in the Trimmer plan
          exports folder. That is a saved plan, <strong>not</strong> cutting the video and <strong>not</strong> running
          FFmpeg.
        </p>
        <p className="text-[var(--mm-text2)]">
          Trimmer jobs only run when the server turns on Trimmer workers in its configuration (
          <code className="rounded bg-[var(--mm-surface2)] px-1 py-0.5 text-[0.85em]">MEDIAMOP_TRIMMER_WORKER_COUNT</code>
          ).
        </p>
      </section>
    </div>
  );
}

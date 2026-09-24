/**
 * A library read a page at a time. Everything on the other pages is one click away and the count says how many
 * there are, so a big library never looks smaller than it is (#689).
 */

/** "Showing 201–400 of 1,234", or nothing when every file fits on one page. */
export function pageRange(
  page: number,
  pageSize: number,
  shown: number,
  total: number,
): string | null {
  if (total <= pageSize && page === 1) return null;
  if (shown === 0) return `Showing none of ${total.toLocaleString()}`;
  const first = (page - 1) * pageSize + 1;
  const last = first + shown - 1;
  return `Showing ${first.toLocaleString()}–${last.toLocaleString()} of ${total.toLocaleString()}`;
}

export function LibraryPager({
  page,
  pageSize,
  shown,
  total,
  loading,
  hasSelection,
  onPage,
}: {
  page: number;
  pageSize: number;
  /** Files on this page. */
  shown: number;
  /** Files that match the filters, on every page. */
  total: number;
  /** A page is being read; the buttons wait for it rather than skipping pages unseen. */
  loading: boolean;
  hasSelection: boolean;
  onPage: (page: number) => void;
}) {
  const range = pageRange(page, pageSize, shown, total);
  if (range === null) return null;
  const hasNext = page * pageSize < total;

  return (
    <nav
      className="mm-library-pager"
      aria-label="Pages of files"
      data-testid="library-pager"
    >
      <p className="mm-library-pager__range" data-testid="library-range">
        {range}
      </p>
      <button
        type="button"
        className="mm-head-control"
        disabled={loading || page <= 1}
        onClick={() => onPage(page - 1)}
      >
        Previous
      </button>
      <button
        type="button"
        className="mm-head-control"
        disabled={loading || !hasNext}
        onClick={() => onPage(page + 1)}
      >
        Next
      </button>
      {hasSelection ? (
        <p className="mm-library-pager__note">
          Your selection is for this page: moving to another page clears it.
        </p>
      ) : null}
    </nav>
  );
}

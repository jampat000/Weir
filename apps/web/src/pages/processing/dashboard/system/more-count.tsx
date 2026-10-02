/** "N more" beside a card's title: how many rows the card's height left out. Nothing when every row shows. */
export function MoreCount({ count }: { count: number }) {
  if (count <= 0) return null;
  return (
    <span className="mm-sy-more" data-testid="system-more">
      {count.toLocaleString()} more
    </span>
  );
}

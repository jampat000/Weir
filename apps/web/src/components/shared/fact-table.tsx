export type Fact = {
  label: string;
  value: string;
  detail?: string;
  /** An mm-status-text--* class when the value itself is good or bad news. */
  toneClass?: string;
};

/**
 * Coequal facts as one borderless label/value row: answers to different questions (a policy, a
 * version, a count), so none of them is a hero. Below the narrow breakpoint the quiet table stacks
 * each column into its own labelled line.
 */
export function FactTable({
  facts,
  caption,
  "data-testid": dataTestId,
}: {
  facts: readonly Fact[];
  caption: string;
  "data-testid"?: string;
}) {
  return (
    <div className="mm-quiet-table-wrap">
      <table className="mm-quiet-table" data-testid={dataTestId}>
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>
            {facts.map((fact) => (
              <th scope="col" key={fact.label}>
                {fact.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          <tr>
            {facts.map((fact) => (
              <td key={fact.label} data-label={fact.label}>
                <span>
                  <span
                    className={`mm-quiet-table__strong${fact.toneClass ? ` ${fact.toneClass}` : ""}`}
                  >
                    {fact.value}
                  </span>
                  {fact.detail ? (
                    <span className="mm-quiet-table__sub">{fact.detail}</span>
                  ) : null}
                </span>
              </td>
            ))}
          </tr>
        </tbody>
      </table>
    </div>
  );
}

/** Honest shell placeholder: title + one line, no fake UI. */

type Props = { title: string; lead: string };

export function MinimalShellPlaceholder({ title, lead }: Props) {
  return (
    <div className="mm-page">
      <header className="mm-page__intro">
        <h1 className="mm-page__title">{title}</h1>
        <p className="mm-page__lead">{lead}</p>
      </header>
    </div>
  );
}

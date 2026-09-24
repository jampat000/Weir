/**
 * What a Settings panel shows when its data did not arrive, so a failed load never looks like
 * "nothing set up yet". `what` completes "Weir couldn't load your …", e.g. "media managers".
 */
export function SettingsLoadError({ what }: { what: string }) {
  return (
    <ul className="mm-interrupt" role="alert" data-testid="settings-load-error">
      <li className="mm-interrupt__item">
        <span className="mm-interrupt__text">
          Weir couldn&rsquo;t load your {what}. Reload the page to try again.
        </span>
      </li>
    </ul>
  );
}

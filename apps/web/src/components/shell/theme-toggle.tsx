import {
  persistAppTheme,
  useAppTheme,
  type AppTheme,
} from "../../lib/ui/app-theme";
import { useSetThemeMutation } from "../../lib/auth/queries";

/**
 * Light or dark, from every page's header. It sits beside Pause at the same height, so
 * both share `.mm-head-control`. The icon shows the theme you are in; the label says where a
 * click goes. The switch itself never waits on the network (#697): it applies straight away, then
 * saves to the account in the background so the choice follows to another browser or device.
 */
const SAVE_FAILED_TEXT =
  "Weir couldn't save your theme for other browsers. It's changed here for now.";

export function ThemeToggle() {
  const theme = useAppTheme();
  const next: AppTheme = theme === "dark" ? "light" : "dark";
  const setTheme = useSetThemeMutation();

  function choose() {
    persistAppTheme(next);
    setTheme.mutate(next);
  }

  return (
    <div className="mm-theme-control">
      <button
        type="button"
        className="mm-head-control mm-head-control--icon"
        data-testid="theme-toggle"
        aria-label={`Switch to ${next} mode`}
        title={`Switch to ${next} mode`}
        onClick={choose}
      >
        {theme === "dark" ? (
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
          >
            <path
              d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"
              stroke="currentColor"
              strokeWidth="1.8"
              strokeLinejoin="round"
            />
          </svg>
        ) : (
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
          >
            <circle
              cx="12"
              cy="12"
              r="4"
              stroke="currentColor"
              strokeWidth="1.8"
            />
            <path
              d="M12 2.5v2.2M12 19.3v2.2M2.5 12h2.2M19.3 12h2.2M5.3 5.3l1.6 1.6M17.1 17.1l1.6 1.6M5.3 18.7l1.6-1.6M17.1 6.9l1.6-1.6"
              stroke="currentColor"
              strokeWidth="1.8"
              strokeLinecap="round"
            />
          </svg>
        )}
      </button>
      {setTheme.isError ? (
        <p
          className="mm-theme-alert mm-status-text--failed"
          role="alert"
          data-testid="theme-toggle-alert"
        >
          {SAVE_FAILED_TEXT}
        </p>
      ) : null}
    </div>
  );
}

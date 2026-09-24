import {
  persistAppTheme,
  useAppTheme,
  type AppTheme,
} from "../../lib/ui/app-theme";

/**
 * Light or dark, from the title row of every page. It sits beside Pause at the same height, so
 * both share `.mm-head-control`. The icon shows the theme you are in; the label says where a
 * click goes.
 */
export function ThemeToggle() {
  const theme = useAppTheme();
  const next: AppTheme = theme === "dark" ? "light" : "dark";

  return (
    <button
      type="button"
      className="mm-head-control mm-head-control--icon"
      data-testid="theme-toggle"
      aria-label={`Switch to ${next} mode`}
      title={`Switch to ${next} mode`}
      onClick={() => persistAppTheme(next)}
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
  );
}

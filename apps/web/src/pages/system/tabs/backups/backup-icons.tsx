/** The two marks on a backup's row: a tray with an arrow down for downloading it, a turn back for restoring it. */

const ICON_SIZE = 16;

function Icon({ children }: { children: React.ReactNode }) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={ICON_SIZE}
      height={ICON_SIZE}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {children}
    </svg>
  );
}

export function DownloadIcon() {
  return (
    <Icon>
      <path d="M12 4v11" />
      <path d="m7 11 5 5 5-5" />
      <path d="M5 20h14" />
    </Icon>
  );
}

export function RestoreIcon() {
  return (
    <Icon>
      <path d="M4 12a8 8 0 1 1 2.6 5.9" />
      <path d="M4 5v5h5" />
    </Icon>
  );
}

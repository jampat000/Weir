type NavigatorWithUserAgentData = Navigator & {
  userAgentData?: { platform?: string };
};

/** Whether this browser runs on Windows, so an example path can be written the way its user types one. */
export function isWindowsBrowser(): boolean {
  const { userAgentData, userAgent } = navigator as NavigatorWithUserAgentData;
  return (userAgentData?.platform || userAgent).toLowerCase().includes("win");
}

/** An example folder path in the shape this browser's user expects. */
export function examplePath(windows: string, posix: string): string {
  return isWindowsBrowser() ? windows : posix;
}

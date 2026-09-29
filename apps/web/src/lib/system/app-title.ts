const APP_NAME = "Weir";

/** The app named after the computer it runs on, as the browser tab and sidebar show it: "Weir · RIG". */
export function appTitle(machineName: string | undefined): string {
  const machine = machineName?.trim();
  return machine ? `${APP_NAME} · ${machine}` : APP_NAME;
}

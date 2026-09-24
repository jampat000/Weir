import { afterEach, describe, expect, it, vi } from "vitest";
import {
  deleteNotificationChannel,
  configurationBackupsPath,
  configurationBundlePath,
  securityOverviewPath,
  appSettingsPath,
  updateStatusPath,
} from "./settings-api";

afterEach(() => {
  vi.restoreAllMocks();
});

describe("suite settings API paths", () => {
  it("uses suite settings and security-overview routes", () => {
    expect(appSettingsPath()).toBe("/api/v1/suite/settings");
    expect(securityOverviewPath()).toBe("/api/v1/suite/security-overview");
    expect(configurationBundlePath()).toBe(
      "/api/v1/suite/configuration-bundle",
    );
    expect(configurationBackupsPath()).toBe(
      "/api/v1/suite/configuration-backups",
    );
    // One address per handler, so a 404 means the request is wrong rather than a missing alias.
    expect(updateStatusPath()).toBe("/api/v1/suite/update-status");
  });

  it("sends a CSRF header when deleting a notification channel", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ csrf_token: "csrf-test" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      )
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await deleteNotificationChannel(7);

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const [, request] = fetchMock.mock.calls[1] as [string, RequestInit];
    expect(request.method).toBe("DELETE");
    expect(request.headers).toMatchObject({ "X-CSRF-Token": "csrf-test" });
  });
});

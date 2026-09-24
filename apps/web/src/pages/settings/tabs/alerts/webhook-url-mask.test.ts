import { expect, it } from "vitest";

import { maskWebhookUrl } from "./webhook-url-mask";

it("collapses a Discord-shaped webhook URL to its host and shape, hiding the token", () => {
  expect(
    maskWebhookUrl(
      "https://discord.com/api/webhooks/123456789012345678/abcDEF-token_123",
    ),
  ).toBe("discord.com/api/webhooks/…/•••• (saved)");
});

it("falls back to the host when the path has only one segment", () => {
  expect(maskWebhookUrl("https://example.invalid/onlytoken")).toBe(
    "example.invalid/… (saved)",
  );
});

it("masks conservatively when the value is not a parseable URL", () => {
  expect(maskWebhookUrl("not a url")).toBe("•••• (saved)");
});

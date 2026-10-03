import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { CopyLink } from "./copy-link";

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe("CopyLink", () => {
  it("copies the value, says so for a moment, then goes back to Copy", async () => {
    vi.useFakeTimers();
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    render(<CopyLink value="http://10.1.1.196:9347" label="the address" />);

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Copy the address" }));
    });

    expect(writeText).toHaveBeenCalledWith("http://10.1.1.196:9347");
    expect(screen.getByRole("button")).toHaveTextContent("Copied");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2_000);
    });

    expect(screen.getByRole("button")).toHaveTextContent("Copy");
  });

  it("stays Copy when the browser will not let the page use the clipboard", async () => {
    const writeText = vi.fn().mockRejectedValue(new Error("denied"));
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    render(<CopyLink value="x" label="x" />);

    fireEvent.click(screen.getByRole("button"));
    await vi.waitFor(() => expect(writeText).toHaveBeenCalled());

    expect(screen.getByRole("button")).toHaveTextContent("Copy");
  });

  it("is disabled when there is nothing to copy", () => {
    render(<CopyLink value="" label="the folder" />);

    expect(
      screen.getByRole("button", { name: "Copy the folder" }),
    ).toBeDisabled();
  });
});

import { render, screen } from "@testing-library/react";
import { expect, it } from "vitest";

import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { ConfirmDialog } from "./confirm-dialog";

function renderDialog(tone?: "default" | "danger") {
  render(
    <ConfirmDialog
      title="Remove Movies?"
      confirmLabel="Remove workflow"
      tone={tone}
      testId="remove-dialog"
      onCancel={() => undefined}
      onConfirm={() => undefined}
    />,
  );
  return screen.getByRole("button", { name: "Remove workflow" });
}

it("draws the confirming button in the primary colour by default", () => {
  const confirm = renderDialog();

  expect(confirm).toHaveClass(
    ...mmActionButtonClass({ variant: "primary" }).split(" "),
  );
});

it("draws the confirming button of a destructive action in the failure colour", () => {
  const confirm = renderDialog("danger");

  expect(confirm).toHaveClass(
    ...mmActionButtonClass({ variant: "danger" }).split(" "),
  );
});

it("leaves the cancel button neutral and focused when the action is destructive", () => {
  renderDialog("danger");
  const cancel = screen.getByRole("button", { name: "Keep it" });

  expect(cancel).toHaveClass(
    ...mmActionButtonClass({ variant: "secondary" }).split(" "),
  );
  expect(cancel).toHaveFocus();
});

import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Field } from "./field";

describe("Field", () => {
  it("names the control by its label alone, not by a hint that mentions another field", () => {
    render(
      <>
        <Field label="Output folder" width="wide">
          <input />
        </Field>
        <Field
          label="Work folder"
          width="wide"
          hint="Put it on the same volume as the output folder so finished files move instead of copying."
        >
          <input />
        </Field>
      </>,
    );

    expect(
      screen.getByRole("textbox", { name: "Output folder" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("textbox", { name: "Work folder" }),
    ).toBeInTheDocument();
  });

  it("links the hint to the control with aria-describedby", () => {
    render(
      <Field
        label="Work folder"
        width="wide"
        hint="Leave empty to use Weir's private temporary folder."
      >
        <input />
      </Field>,
    );

    const input = screen.getByRole("textbox", { name: "Work folder" });
    const describedBy = input.getAttribute("aria-describedby");
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy!)).toHaveTextContent(
      "Leave empty to use Weir's private temporary folder.",
    );
  });
});

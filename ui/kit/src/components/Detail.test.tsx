import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CodeBlock, DetailSection, KeyValueRows } from "./Detail";

describe("the journal detail anatomy (v1.3 §9.4)", () => {
  it("sections carry small-caps headings; key-value rows read as one card", () => {
    render(
      <DetailSection title="Identity">
        <KeyValueRows rows={[{ key: "Dedup key", value: "smoke:alert:1", mono: true }]} />
      </DetailSection>,
    );
    expect(screen.getByRole("heading", { name: "Identity" })).toHaveClass("uppercase");
    expect(screen.getByText("Dedup key")).toBeInTheDocument();
    expect(screen.getByText("smoke:alert:1")).toHaveClass("font-mono");
  });

  it("the code block copies its OWN text and says so", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });
    render(<CodeBlock text='{"amount":4200}' />);

    await userEvent.click(screen.getByRole("button", { name: "Copy" }));
    expect(writeText).toHaveBeenCalledWith('{"amount":4200}');
    expect(await screen.findByRole("button", { name: "Copied" })).toBeInTheDocument();
  });
});

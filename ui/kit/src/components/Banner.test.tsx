import { render, screen } from "@testing-library/react";
import { Banner } from "./Banner";

describe("the banner (the kit's one message box)", () => {
  it("defaults to alert — something the operator must act on", () => {
    render(<Banner tone="danger">the page could not be loaded</Banner>);
    expect(screen.getByRole("alert")).toHaveAttribute("data-tone", "danger");
  });

  it("a settled outcome is a status, not an alert", () => {
    render(<Banner tone="success" live="status">run 42 scheduled</Banner>);
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("dense is the inline pill beside a control; the default is the full box", () => {
    const { rerender } = render(<Banner tone="warning" dense>short</Banner>);
    expect(screen.getByRole("alert").className).toContain("text-xs");

    rerender(<Banner tone="warning">long</Banner>);
    expect(screen.getByRole("alert").className).toContain("text-sm");
  });
});

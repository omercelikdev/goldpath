import { render, screen } from "@testing-library/react";
import { StateBadge } from "./components/StateBadge";
import { statusTone } from "./status";

describe("the domain-state → ramp mapping (ui-standard-v1 §5)", () => {
  it.each([
    ["Completed", "success"],
    ["Executed", "success"],
    ["PendingApproval", "warning"],
    ["Validated", "warning"],
    ["CompletedWithFailures", "danger"],
    ["Running", "info"],
    ["Resumed", "violet"],
  ])("%s → %s", (state, tone) => {
    expect(statusTone(state)).toBe(tone);
  });

  it("unknown states are honest neutrals, never a guess", () => {
    expect(statusTone("SomethingNew")).toBe("neutral");
  });

  it("adopter vocabulary extends the map without replacing it", () => {
    expect(statusTone("Settled", { Settled: "success" })).toBe("success");
    expect(statusTone("Completed", { Settled: "success" })).toBe("success");
  });

  it("the badge renders the state text with its tone", () => {
    render(<StateBadge state="PendingApproval" />);
    const badge = screen.getByText("PendingApproval");
    expect(badge).toHaveAttribute("data-tone", "warning");
  });
});

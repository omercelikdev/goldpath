import { render, screen } from "@testing-library/react";
import { AuditBlock, isClassified, SYSTEM_ACTOR, type AuditEntry } from "./AuditBlock";

const entry = (over: Partial<AuditEntry> = {}): AuditEntry => ({
  id: 1,
  timestamp: "2026-07-26T10:00:00Z",
  user: "ops-chief",
  correlationId: "corr-42",
  entityType: "PaymentInstruction",
  entityKey: "8814",
  action: "Modified",
  propertyName: "Status",
  oldValue: "PendingApproval",
  newValue: "Executed",
  ...over,
});

describe("the audit block (ui-standard-v1 §4 — old→new rows, classified masked)", () => {
  it("renders the change as old → new with its actor and correlation id", () => {
    render(<AuditBlock entries={[entry()]} />);

    expect(screen.getByText("Status")).toBeInTheDocument();
    expect(screen.getByText("PendingApproval")).toBeInTheDocument();
    expect(screen.getByText("Executed")).toBeInTheDocument();
    expect(screen.getByText("ops-chief")).toBeInTheDocument();
    expect(screen.getByText(/corr-42/)).toBeInTheDocument();
  });

  it("NEVER puts a classified value in the DOM — masked, not styled over", () => {
    const { container } = render(
      <AuditBlock
        entries={[entry({ propertyName: "NationalId", oldValue: "12345678901", newValue: "10987654321" })]}
        classified={["nationalid"]}   // case-insensitive by design
      />,
    );

    expect(container.textContent).not.toContain("12345678901");
    expect(container.textContent).not.toContain("10987654321");
    expect(screen.getAllByTitle(/classified/i)).toHaveLength(2);   // both sides masked
    expect(screen.getByText("(classified)")).toBeInTheDocument();
  });

  it("a null user is the SYSTEM actor, never an unknown human", () => {
    render(<AuditBlock entries={[entry({ user: null })]} />);

    expect(screen.getByText(SYSTEM_ACTOR)).toBeInTheDocument();
  });

  it("an absent value reads as absent — not as an empty string", () => {
    render(<AuditBlock entries={[entry({ action: "Added", oldValue: null })]} />);

    expect(screen.getByText("∅")).toBeInTheDocument();
    expect(screen.getByText("Executed")).toBeInTheDocument();
  });

  it("classification matching is case-insensitive and exact — no accidental prefix masking", () => {
    expect(isClassified("NationalId", ["nationalid"])).toBe(true);
    expect(isClassified("nationalid", ["NationalId"])).toBe(true);
    expect(isClassified("NationalIdHash", ["NationalId"])).toBe(false);   // a different property
    expect(isClassified("Status", undefined)).toBe(false);
  });

  it("shows the empty message when nothing was audited", () => {
    render(<AuditBlock entries={[]} emptyMessage="No audited changes yet." />);

    expect(screen.getByText("No audited changes yet.")).toBeInTheDocument();
  });
});

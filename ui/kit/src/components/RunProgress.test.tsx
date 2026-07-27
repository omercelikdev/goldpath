import { render, screen } from "@testing-library/react";
import { StateBadge } from "./StateBadge";
import { RunProgress, deadlineVerdict, itemsPerSecond, type RunProgressData } from "./RunProgress";

const base: RunProgressData = {
  status: "Running",
  startedAt: "2026-07-26T10:00:00Z",
  totalChunks: 10,
  completedChunks: 4,
  failedChunks: 0,
  totalItems: 100_000,
  itemFailures: 0,
};

const now = new Date("2026-07-26T10:01:40Z");   // 100 seconds in

describe("the run progress composite (ui-standard-v1 §4)", () => {
  it("computes the rate from the CHUNK share of planned items", () => {
    // 4/10 chunks of 100k items in 100s = 40 000 items / 100s = 400 items/s.
    expect(itemsPerSecond(base, now)).toBeCloseTo(400);
  });

  it("has no rate before the first chunk completes, or without an item plan", () => {
    expect(itemsPerSecond({ ...base, completedChunks: 0 }, now)).toBeNull();
    expect(itemsPerSecond({ ...base, totalItems: null }, now)).toBeNull();
    expect(itemsPerSecond({ ...base, startedAt: now.toISOString() }, now)).toBeNull();   // zero elapsed
  });

  it("a finished run's rate is measured over its OWN window, not until now", () => {
    const finished = { ...base, completedChunks: 10, finishedAt: "2026-07-26T10:00:50Z" };
    expect(itemsPerSecond(finished, now)).toBeCloseTo(2000);   // 100k in 50s, not in 100s
  });

  it("judges the deadline: live runs on the clock and the prediction, finished runs on reality", () => {
    // The clock is INJECTED: a verdict that silently reads the wall clock cannot be tested
    // and would drift under a skewed browser.
    const now = new Date("2026-07-26T10:10:00Z");
    expect(deadlineVerdict(base, now)).toBe("none");                                    // no deadline
    expect(deadlineVerdict({ ...base, deadlineAt: "2026-07-26T11:00:00Z" }, now)).toBe("on-track");
    expect(deadlineVerdict({
      ...base,
      deadlineAt: "2026-07-26T10:30:00Z",
      predictedFinishAt: "2026-07-26T10:45:00Z",
    }, now)).toBe("overrun-predicted");

    // Still running AFTER its deadline: it has overrun, prediction or not. Anything else
    // would tell the operator the one thing that is certainly false.
    expect(deadlineVerdict({ ...base, deadlineAt: "2026-07-26T10:05:00Z" }, now)).toBe("overrun");
    expect(deadlineVerdict({
      ...base,
      deadlineAt: "2026-07-26T10:05:00Z",
      predictedFinishAt: "2026-07-26T10:04:00Z",
    }, now)).toBe("overrun");
    expect(deadlineVerdict({
      ...base,
      status: "Completed",
      deadlineAt: "2026-07-26T10:30:00Z",
      predictedFinishAt: "2026-07-26T10:45:00Z",
      finishedAt: "2026-07-26T10:20:00Z",
    }, now)).toBe("on-track");                                                          // reality beat the prediction
    expect(deadlineVerdict({
      ...base,
      status: "Completed",
      deadlineAt: "2026-07-26T10:30:00Z",
      finishedAt: "2026-07-26T10:44:00Z",
    }, now)).toBe("overrun");
  });

  it("renders chunk progress, the rate and the predicted-overrun warning", () => {
    render(
      <RunProgress
        run={{ ...base, deadlineAt: "2026-07-26T10:30:00Z", predictedFinishAt: "2026-07-26T10:45:00Z" }}
        now={now}
      />,
    );

    expect(screen.getByText("4/10 chunks")).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "40");
    expect(screen.getByText("400 items/s")).toBeInTheDocument();
    expect(screen.getByText("predicted to overrun")).toBeInTheDocument();
  });

  it("the BADGE carries the predicted-overrun warning (§5's composite state)", () => {
    const { rerender } = render(<RunProgress run={{ ...base, deadlineAt: "2026-07-26T11:00:00Z" }} now={now} />);
    expect(screen.getByText("Running")).toHaveAttribute("data-tone", "info");   // plain Running

    rerender(
      <RunProgress
        run={{ ...base, deadlineAt: "2026-07-26T10:30:00Z", predictedFinishAt: "2026-07-26T10:45:00Z" }}
        now={now}
      />,
    );
    expect(screen.getByText("Running")).toHaveAttribute("data-tone", "warning");
  });

  it("the tone override does NOT weaken the MAP-wins rule for adopter vocabulary", () => {
    render(<StateBadge state="Running" extra={{ Running: "danger" }} />);
    expect(screen.getByText("Running")).toHaveAttribute("data-tone", "info");   // MAP still wins
  });

  it("surfaces failed chunks and repair-queue depth — never hides them in the bar", () => {
    render(<RunProgress run={{ ...base, failedChunks: 2, itemFailures: 37 }} now={now} />);

    expect(screen.getByText("2 failed")).toBeInTheDocument();
    expect(screen.getByText("37 items in repair")).toBeInTheDocument();
  });

  it("survives a zero-chunk plan without dividing by zero", () => {
    render(<RunProgress run={{ ...base, totalChunks: 0, completedChunks: 0 }} now={now} />);

    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "0");
    expect(screen.getByText("0/0 chunks")).toBeInTheDocument();
  });

  it("an overrun that ALREADY happened reads danger; one merely predicted reads warning", () => {
    const overrun = render(
      <RunProgress
        run={{
          status: "Running", startedAt: "2026-07-27T00:00:00Z",
          deadlineAt: "2026-07-27T01:00:00Z", totalChunks: 10, completedChunks: 3, failedChunks: 0, itemFailures: 0,
        }}
        now={new Date("2026-07-27T02:00:00Z")}
      />,
    );
    expect(overrun.container.querySelector(".text-danger")).not.toBeNull();
    overrun.unmount();

    const predicted = render(
      <RunProgress
        run={{
          status: "Running", startedAt: "2026-07-27T00:00:00Z",
          deadlineAt: "2026-07-27T01:00:00Z", predictedFinishAt: "2026-07-27T01:30:00Z",
          totalChunks: 10, completedChunks: 3, failedChunks: 0, itemFailures: 0,
        }}
        now={new Date("2026-07-27T00:30:00Z")}
      />,
    );
    expect(predicted.container.querySelector(".text-warning")).not.toBeNull();
    expect(predicted.container.querySelector(".text-danger")).toBeNull();
  });
});

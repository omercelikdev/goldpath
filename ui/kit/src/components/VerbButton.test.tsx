import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { VerbOutcome } from "../adminResult";
import { VerbButton } from "./VerbButton";

const ok = (message: string): VerbOutcome => ({ kind: "ok", message });
const refused = (message: string): VerbOutcome => ({ kind: "refused", message });

describe("the verb button (ui-standard-v1 §3/§4 — confirm-before-verb, verbatim refusals)", () => {
  it("NEVER executes without the confirm step, and cancel backs out untouched", async () => {
    const execute = vi.fn(async () => ok("done"));
    render(<VerbButton label="trigger" confirm="Trigger the nightly run?" execute={execute} />);

    await userEvent.click(screen.getByRole("button", { name: "trigger" }));
    expect(execute).not.toHaveBeenCalled();                          // confirming, not executing
    expect(screen.getByRole("alertdialog")).toHaveTextContent("Trigger the nightly run?");
    expect(screen.getByRole("alertdialog")).toHaveTextContent("audited");   // the audit hint

    await userEvent.click(screen.getByRole("button", { name: "cancel" }));
    expect(execute).not.toHaveBeenCalled();
    expect(screen.queryByRole("alertdialog")).toBeNull();
  });

  it("confirm → execute → the ok message surfaces and onDone fires", async () => {
    const execute = vi.fn(async () => ok("run 42 scheduled"));
    const onDone = vi.fn();
    render(<VerbButton label="trigger" confirm="Sure?" execute={execute} onDone={onDone} />);

    await userEvent.click(screen.getByRole("button", { name: "trigger" }));
    await userEvent.click(screen.getByRole("alertdialog").querySelector("button")!);

    expect(await screen.findByRole("status")).toHaveTextContent("run 42 scheduled");
    expect(execute).toHaveBeenCalledTimes(1);
    expect(onDone).toHaveBeenCalledWith(ok("run 42 scheduled"));
  });

  it("a refusal surfaces the envelope message VERBATIM — teaching text untouched", async () => {
    const teaching = "the batch is not Validated — approve requires the validation gate to have passed";
    const execute = vi.fn(async () => refused(teaching));
    render(<VerbButton label="approve" confirm="Approve?" execute={execute} />);

    await userEvent.click(screen.getByRole("button", { name: "approve" }));
    await userEvent.click(screen.getByRole("alertdialog").querySelector("button")!);

    expect(await screen.findByRole("alert")).toHaveTextContent(teaching);
  });

  it("a transport failure says the verb MAY NOT have run — never a silent swallow", async () => {
    const execute = vi.fn(async () => {
      throw new Error("network");
    });
    render(<VerbButton label="pause" confirm="Pause?" execute={execute} />);

    await userEvent.click(screen.getByRole("button", { name: "pause" }));
    await userEvent.click(screen.getByRole("alertdialog").querySelector("button")!);

    expect(await screen.findByRole("alert")).toHaveTextContent(/may not have run/i);
  });

  it("a settled verb can be confirmed again — the outcome strip resets", async () => {
    const execute = vi
      .fn<() => Promise<VerbOutcome>>()
      .mockResolvedValueOnce(refused("not yet"))
      .mockResolvedValueOnce(ok("now it worked"));
    render(<VerbButton label="resume" confirm="Resume?" execute={execute} />);

    await userEvent.click(screen.getByRole("button", { name: "resume" }));
    await userEvent.click(screen.getByRole("alertdialog").querySelector("button")!);
    expect(await screen.findByRole("alert")).toHaveTextContent("not yet");

    await userEvent.click(screen.getByRole("button", { name: "resume" }));
    await userEvent.click(screen.getByRole("alertdialog").querySelector("button")!);
    expect(await screen.findByRole("status")).toHaveTextContent("now it worked");
    expect(screen.queryByRole("alert")).toBeNull();   // the old refusal strip is gone
  });

  it("an evidence note is handed to the verb and cleared afterwards", async () => {
    const seen: (string | undefined)[] = [];
    render(
      <VerbButton
        label="reject"
        confirm="Reject this batch?"
        note={{ label: "reason (required)", required: true }}
        execute={(note) => {
          seen.push(note);
          return Promise.resolve({ kind: "ok", message: "rejected" } as VerbOutcome);
        }}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "reject" }));
    const dialog = screen.getByRole("alertdialog");
    // A required note is the gate: the verb cannot fire until the reason exists.
    expect(within(dialog).getByRole("button", { name: "reject" })).toBeDisabled();

    await userEvent.type(within(dialog).getByLabelText("reason (required)"), "  duplicate file  ");
    await userEvent.click(within(dialog).getByRole("button", { name: "reject" }));

    expect(await screen.findByText("rejected")).toBeInTheDocument();
    expect(seen).toEqual(["duplicate file"]);   // trimmed — surrounding space is not evidence

    // The next confirm starts empty: a stale reason must never ride along on another verb.
    await userEvent.click(screen.getByRole("button", { name: "reject" }));
    expect(within(screen.getByRole("alertdialog")).getByLabelText("reason (required)")).toHaveValue("");
  });

  it("an optional note lets the verb fire without one", async () => {
    const seen: (string | undefined)[] = [];
    render(
      <VerbButton
        label="approve"
        confirm="Approve?"
        note={{ label: "note (optional)" }}
        execute={(note) => {
          seen.push(note);
          return Promise.resolve({ kind: "ok", message: "approved" } as VerbOutcome);
        }}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "approve" }));
    await userEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "approve" }));

    expect(await screen.findByText("approved")).toBeInTheDocument();
    expect(seen).toEqual([""]);
  });

  it("a quiet verb stays silent — its composite owns the outcome", async () => {
    const seen: VerbOutcome[] = [];
    render(<VerbButton label="approve" confirm="Approve?" quiet execute={() => Promise.resolve(ok("approved"))} onDone={(o) => seen.push(o)} />);

    await userEvent.click(screen.getByRole("button", { name: "approve" }));
    await userEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "approve" }));

    // The outcome still REACHES the composite; only the button's own strip is suppressed,
    // because a gate button unmounts the moment its verb changes the state.
    await waitFor(() => expect(seen).toEqual([ok("approved")]));
    expect(screen.queryByText("approved")).toBeNull();
  });

  it("an unexpected STATUS is named — the operator gets a number to take to the logs", async () => {
    render(
      <VerbButton
        label="trigger"
        confirm="Trigger?"
        execute={() => Promise.resolve({ kind: "error", status: 503 } as VerbOutcome)}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "trigger" }));
    await userEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "trigger" }));

    // Status 0 means "never left the browser"; a real status means the service answered
    // something unexpected — two different stories, told differently.
    expect(await screen.findByText(/unexpected 503 — check the service logs/)).toBeInTheDocument();
  });
});

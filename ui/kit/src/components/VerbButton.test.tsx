import { render, screen } from "@testing-library/react";
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
});

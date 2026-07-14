import { executeVerb } from "./adminResult";

const json = (status: number, body: unknown) =>
  Promise.resolve(new Response(JSON.stringify(body), { status }));

describe("the frozen verb envelope (goldpath-admin-contract)", () => {
  it("200 + ok:true is success, message passed through verbatim", async () => {
    const outcome = await executeVerb("/x", {}, () => json(200, { ok: true, message: "triggered" }));
    expect(outcome).toEqual({ kind: "ok", message: "triggered" });
  });

  it("400 + ok:false is a TEACHING refusal, never an exception", async () => {
    const outcome = await executeVerb("/x", {}, () =>
      json(400, { ok: false, message: "the submitter cannot approve their own instruction" }));
    expect(outcome).toEqual({ kind: "refused", message: "the submitter cannot approve their own instruction" });
  });

  it("anything else is a transport error carrying its status", async () => {
    const outcome = await executeVerb("/x", {}, () => json(503, {}));
    expect(outcome).toEqual({ kind: "error", status: 503 });
  });
});

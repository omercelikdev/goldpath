import type { VerbOutcome } from "@goldpath/kit";

/**
 * The frozen verb envelope, adapted to the kit's outcome type. A refusal is DATA, not an
 * exception: the server's own sentence is what the operator reads.
 */
export async function asOutcome(call: Promise<{ ok: boolean; message: string }>): Promise<VerbOutcome> {
  const result = await call;
  return result.ok ? { kind: "ok", message: result.message } : { kind: "refused", message: result.message };
}

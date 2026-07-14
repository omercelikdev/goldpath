// The FROZEN verb envelope (docs/rfc/goldpath-admin-contract.md): every mutating verb
// answers { ok, message } — 200 when ok, 400 with the same envelope when refused.
// Refusals TEACH; the kit surfaces the message verbatim, never paraphrased.
export interface GoldpathAdminResult {
  ok: boolean;
  message: string;
}

export type VerbOutcome =
  | { kind: "ok"; message: string }
  | { kind: "refused"; message: string }
  | { kind: "error"; status: number };

/** Executes one admin verb per the frozen contract's envelope semantics. */
export async function executeVerb(
  url: string,
  init: RequestInit,
  fetcher: typeof fetch = fetch,
): Promise<VerbOutcome> {
  const response = await fetcher(url, { method: "POST", ...init });
  if (response.status === 200 || response.status === 400) {
    const result = (await response.json()) as GoldpathAdminResult;
    return result.ok ? { kind: "ok", message: result.message } : { kind: "refused", message: result.message };
  }

  return { kind: "error", status: response.status };
}

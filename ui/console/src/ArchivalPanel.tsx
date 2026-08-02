import { Trash2 } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Banner, Checkbox, CodeBlock, DetailSection, KeyValueRows, KeysetTable, Select, StateBadge, Table, VerbButton, humanizeSeconds } from "@goldpath/kit";
import type { VerbOutcome } from "@goldpath/kit";
import type {
  AdminClient,
  ArchiveDefinitionStatus,
  ArchiveEntry,
  ChainFinding,
  ErasureRecord,
  LegalHold,
} from "./adminClient";

export interface ArchivalPanelProps {
  client: AdminClient;
  /** Injected in tests; only used to age the hold list. */
  now?: Date;
}

/** The verb envelope, adapted to the kit's outcome type — refusals stay data. */
async function asOutcome(call: Promise<{ ok: boolean; message: string }>): Promise<VerbOutcome> {
  const result = await call;
  return result.ok ? { kind: "ok", message: result.message } : { kind: "refused", message: result.message };
}

/**
 * The archival panel (console RFC §3): the chain's health, retrieval by key, and the three
 * lifecycle verbs — hold, lift, erase — each of which writes its own evidence row. The
 * archive/purge/verify RUNS live in the run console; what this screen owns is the
 * lifecycle and the proof that the chain is still intact.
 */
export function ArchivalPanel({ client, now }: ArchivalPanelProps) {
  const [definitions, setDefinitions] = useState<ArchiveDefinitionStatus[] | null>(null);
  const [definition, setDefinition] = useState<string>("");
  const [key, setKey] = useState<string>("");
  const [entry, setEntry] = useState<ArchiveEntry | null>(null);
  const [lookedUp, setLookedUp] = useState<string | null>(null);
  const [revealDocument, setRevealDocument] = useState(false);
  const [findings, setFindings] = useState<{ definition: string; findings: ChainFinding[] } | null>(null);
  // A verification that could not RUN is its own state: neither "verifies" nor "broken".
  const [verifyFailed, setVerifyFailed] = useState<string | null>(null);
  const [includeLifted, setIncludeLifted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);
  // A lifecycle verb changes what the entry IS (held, erased), so the buttons around it
  // re-render — the outcome has to live above them.
  const [outcome, setOutcome] = useState<VerbOutcome | null>(null);

  const refresh = () => setRefreshToken((token) => token + 1);
  const settle = (result: VerbOutcome) => {
    setOutcome(result);
    refresh();
  };

  useEffect(() => {
    let live = true;
    client
      .archiveDefinitions()
      .then((found) => {
        if (!live) return;
        setDefinitions(found);
        setDefinition((current) => current || found[0]?.name || "");
      })
      .catch(() => live && setError("the archive definitions could not be loaded"));
    return () => {
      live = false;
    };
  }, [client, refreshToken]);

  // The open entry re-reads on refresh: a hold or an erasure changes the very row shown.
  useEffect(() => {
    if (!lookedUp || !definition) return;
    let live = true;
    client
      .archiveEntry(definition, lookedUp)
      .then((found) => live && setEntry(found))
      .catch(() => live && setError(`entry ${lookedUp} could not be read`));
    return () => {
      live = false;
    };
  }, [client, definition, lookedUp, refreshToken]);

  const loadHolds = useCallback(
    async (_cursor: string | null, take: number) => ({ items: await client.holds(includeLifted, take), nextCursor: null }),
    [client, includeLifted, refreshToken],
  );

  const loadErasures = useCallback(
    async (_cursor: string | null, take: number) => ({ items: await client.erasures(take), nextCursor: null }),
    [client, refreshToken],
  );

  const lookUp = () => {
    const trimmed = key.trim();
    if (!trimmed) return;
    setOutcome(null);
    setRevealDocument(false);
    setEntry(null);
    setLookedUp(trimmed);
  };

  const verify = async (name: string): Promise<VerbOutcome> => {
    // Clear FIRST: a stale verdict from the previous run must never stand in for this one.
    setFindings(null);
    setVerifyFailed(null);
    try {
      const found = await client.verifyChain(name);
      setFindings({ definition: name, findings: found });
      // An empty finding list is the good news, and it is said as such.
      return found.length === 0
        ? { kind: "ok", message: `${name}: the chain verifies — no findings` }
        : { kind: "refused", message: `${name}: ${found.length} chain finding(s) — see below` };
    } catch {
      setVerifyFailed(name);
      return { kind: "error", status: 0 };
    }
  };

  const clock = now ?? new Date();

  return (
    <div data-testid="archival-panel" className="space-y-6">
      {error && <Banner tone="danger">{error}</Banner>}

      <section>
        <h2 className="section-title">Archives</h2>
        <Table
          columns={[
            { header: "Archive", cell: (archive) => <span className="font-medium">{archive.name}</span> },
            { header: "Entries", align: "right", cell: (archive) => <span className="text-xs">{archive.entries}</span> },
            {
              header: "Due",
              align: "right",
              cell: (archive) => (
                <span className={`text-xs ${archive.dueBacklog > 0 ? "text-warning" : "text-faint"}`}>{archive.dueBacklog}</span>
              ),
            },
            { header: "Holds", align: "right", cell: (archive) => <span className="text-xs">{archive.activeHolds}</span> },
            {
              header: "Chain",
              cell: (archive) => (
                // The chain head and the purge watermark together say how much history is provable.
                <span className="text-xs text-faint">head {archive.chainHead} · purged through {archive.purgedThrough}</span>
              ),
            },
            {
              header: "",
              align: "right",
              cell: (archive) => (
                <VerbButton
                  label={`verify ${archive.name}`}
                  confirm={`Verify the ${archive.name} chain end to end? This reads every entry.`}
                  execute={() => verify(archive.name)}
                  // Quiet: the findings section below says it richer, and says it in one place.
                  quiet
                />
              ),
            },
          ]}
          rows={definitions ?? []}
          rowKey={(archive) => archive.name}
          emptyMessage="No archives are defined in this app."
        />
      </section>

      {verifyFailed && (
        <Banner tone="warning" live="alert">
          {verifyFailed}: the verification could not be run — the chain's state is unknown, not proven good.
        </Banner>
      )}

      {findings && (
        <section data-testid="chain-findings">
          {findings.findings.length === 0 ? (
            <Banner tone="success" live="status">
              {findings.definition}: the chain verifies — every entry links to the one before it.
            </Banner>
          ) : (
            <>
              <Banner tone="danger" live="alert">
                {findings.definition}: the chain does NOT verify — {findings.findings.length} finding(s).
              </Banner>
              <ul className="mt-2 space-y-1">
                {findings.findings.map((finding) => (
                  <li key={`${finding.definition}-${finding.chainIndex}`} className="flex flex-wrap items-baseline gap-2 text-xs">
                    <span className="font-mono">#{finding.chainIndex}</span>
                    <span className="font-mono">{finding.aggregateKey}</span>
                    <span className="text-danger">{finding.problem}</span>
                  </li>
                ))}
              </ul>
            </>
          )}
        </section>
      )}

      <section>
        <h2 className="section-title">Retrieve</h2>
        <div className="flex flex-wrap items-end gap-2">
          <label className="flex flex-col gap-1 text-xs text-muted-foreground">
            Archive
            <Select
              aria-label="archive"
              value={definition}
              onChange={(name) => {
                setDefinition(name);
                setEntry(null);
                setLookedUp(null);
              }}
              options={(definitions ?? []).map((archive) => ({ value: archive.name }))}
            />
          </label>
          <label className="flex flex-col gap-1 text-xs text-muted-foreground">
            Aggregate key
            <input
              aria-label="aggregate key"
              className="control w-64"
              value={key}
              onChange={(event) => setKey(event.target.value)}
              onKeyDown={(event) => event.key === "Enter" && lookUp()}
            />
          </label>
          <button
            className="btn-quiet"
            onClick={lookUp}
          >
            retrieve
          </button>
        </div>
        {/* The archive is keyed, not browsable — saying so beats an empty search box. */}
        <p className="mt-1 text-xs text-faint">
          An archive is retrieved by key, never browsed: the contract has no listing route, and the console invents none.
        </p>
      </section>

      {lookedUp && !entry && (
        <Banner tone="info">
          No entry for “{lookedUp}” in {definition} — it may never have been archived, or it may have been purged.
        </Banner>
      )}

      {entry && (
        <section data-testid="archive-entry" className="card">
          <div className="mb-3 flex flex-wrap items-center gap-3">
            <h2 className="text-sm font-medium">
              {entry.definition} · <span className="font-mono">{entry.aggregateKey}</span>
            </h2>
            {entry.erasedAt && <StateBadge state="Erased" tone="warning" />}
            {entry.tenant && <span className="text-xs text-faint">tenant {entry.tenant}</span>}
            <span className="ml-auto flex flex-wrap gap-2">
              <VerbButton
                label="hold"
                confirm={`Place a legal hold on ${entry.aggregateKey}? It will survive retention purges until lifted.`}
                note={{ label: "case reference (required)", required: true }}
                execute={(caseReference) => asOutcome(client.placeHold(entry.definition, entry.aggregateKey, caseReference ?? ""))}
                onDone={settle}
                quiet
              />
              <VerbButton
                label="lift-hold"
                confirm={`Lift the hold on ${entry.aggregateKey}? Retention applies again from now on.`}
                execute={() => asOutcome(client.liftHold(entry.definition, entry.aggregateKey))}
                onDone={settle}
                quiet
              />
              <VerbButton
                label="erase"
                icon={<Trash2 />}
                // Erasure redacts classified fields IN PLACE and re-stamps the content
                // hash; the chain stays verifiable because the sealed hash is kept.
                confirm={`Erase the classified fields of ${entry.aggregateKey}? This cannot be undone — the entry stays in the chain, redacted, and the erasure is recorded.`}
                note={{ label: "subject key (required)", required: true }}
                execute={(subjectKey) => asOutcome(client.erase(entry.definition, entry.aggregateKey, subjectKey ?? ""))}
                onDone={settle}
                quiet
                destructive
              />
            </span>
          </div>

          {outcome && outcome.kind !== "error" && (
            <Banner tone={outcome.kind === "ok" ? "success" : "danger"} live={outcome.kind === "ok" ? "status" : "alert"}>
              {outcome.message}
            </Banner>
          )}
          {outcome?.kind === "error" && (
            <Banner tone="warning">the verb did not reach the server — it may not have been applied</Banner>
          )}

          <DetailSection title="Chain">
            <KeyValueRows
              rows={[
                { key: "Chain index", value: String(entry.chainIndex) },
                { key: "Due / archived", value: `${entry.dueAt} → ${entry.archivedAt}`, mono: true },
                { key: "Schema version", value: String(entry.schemaVersion) },
                { key: "Chain hash (sealed)", value: entry.chainHash, mono: true },
                { key: "Content hash (current)", value: entry.contentHash, mono: true },
                { key: "Previous hash", value: entry.previousHash || "genesis", mono: true },
              ]}
            />
          </DetailSection>

          {entry.erasedAt && (
            // Divergence WITHOUT an erasure stamp is tamper; with one, it is the record
            // of a redaction — the panel spells out which of the two this is.
            <p className="mt-3 text-xs text-warning">
              Redacted {entry.erasedAt}: the current content hash differs from the sealed one BY DESIGN
              {entry.preErasureContentHash ? ` (pre-erasure hash ${entry.preErasureContentHash} is kept, so the chain still verifies)` : ""}.
            </p>
          )}

          <div className="mt-4">
            <button
              className="btn-quiet"
              onClick={() => setRevealDocument((shown) => !shown)}
            >
              {revealDocument ? "hide document" : "reveal document"}
            </button>
            {/* Hidden by DEFAULT: the API returns the whole archived graph, and an operator
                opening a screen should not spray personal data across it by accident. */}
            <span className="ml-2 text-xs text-faint">the archived graph, as stored — it may contain personal data</span>
            {revealDocument && (
              <div className="mt-2 max-h-64 overflow-auto">
                <CodeBlock text={entry.document} />
              </div>
            )}
          </div>
        </section>
      )}

      <section>
        <h2 className="section-title">Legal holds</h2>
        <KeysetTable<LegalHold>
          toolbar={<Checkbox checked={includeLifted} onChange={setIncludeLifted} label="include lifted" />}
          columns={[
            { header: "Archive", cell: (hold) => hold.definition },
            { header: "Key", cell: (hold) => <span className="font-mono text-xs">{hold.aggregateKey}</span> },
            { header: "Case", cell: (hold) => hold.caseReference },
            { header: "Placed", cell: (hold) => `${hold.placedBy} · ${humanizeSeconds((clock.getTime() - Date.parse(hold.placedAt)) / 1000)} ago` },
            {
              header: "State",
              cell: (hold) =>
                hold.liftedAt ? (
                  <span className="text-xs text-faint">lifted by {hold.liftedBy} at {hold.liftedAt}</span>
                ) : (
                  <StateBadge state="Held" tone="warning" />
                ),
            },
          ]}
          loadPage={loadHolds}
          rowKey={(hold) => String(hold.id)}
          emptyMessage={includeLifted ? "No holds have ever been placed." : "No active holds."}
        />
      </section>

      <section>
        <h2 className="section-title">Erasures</h2>
        <KeysetTable<ErasureRecord>
          key={`erasures-${refreshToken}`}
          columns={[
            { header: "Subject", cell: (record) => <span className="font-mono text-xs">{record.subjectKey}</span> },
            { header: "Requested by", cell: (record) => record.requestedBy },
            { header: "At", cell: (record) => record.requestedAt },
            { header: "Entries", align: "right", cell: (record) => record.entriesAffected },
            { header: "Detail", cell: (record) => record.detail ?? "—" },
          ]}
          loadPage={loadErasures}
          rowKey={(record) => String(record.id)}
          emptyMessage="No erasure has been performed."
        />
        {/* The record IS the answer to a subject request — that is why it is a list, not a log line. */}
        <p className="mt-1 text-xs text-faint">
          Each row is the durable answer to one erasure request: who asked, when, and how many entries it touched.
        </p>
      </section>
    </div>
  );
}

import { useCallback, useEffect, useRef, useState } from "react";
import { Banner, humanizeSeconds, KeysetTable, Sheet, StateBadge, Table, VerbButton } from "@goldpath/kit";
import type { VerbOutcome } from "@goldpath/kit";
import type { AdminClient, BulkBatchInfo, BulkDefinitionStatus, BulkRowError } from "./adminClient";

export interface BulkPanelProps {
  client: AdminClient;
}

/** The verb envelope, adapted to the kit's outcome type — refusals stay data. */
async function asOutcome(call: Promise<{ ok: boolean; message: string }>): Promise<VerbOutcome> {
  const result = await call;
  return result.ok ? { kind: "ok", message: result.message } : { kind: "refused", message: result.message };
}

/** The states the intake surface can filter by — the engine's own enum, in its order. */
const STATES = [
  "Received",
  "Validating",
  "Validated",
  "Approved",
  "Rejected",
  "Executing",
  "Completed",
  "CompletedWithFailures",
] as const;

/** The gate only opens on a validated batch; everywhere else the decision is already made. */
const GATED = "Validated";

/**
 * The bulk intake panel (console RFC §3): upload → validation report → the four-eyes gate.
 * The run half of a batch lives in the RUN console (a batch executes as a run) — this
 * screen owns only what the intake surface owns, and links the two by run id.
 */
export function BulkPanel({ client }: BulkPanelProps) {
  const [definitions, setDefinitions] = useState<BulkDefinitionStatus[] | null>(null);
  const [state, setState] = useState<string>("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selected, setSelected] = useState<BulkBatchInfo | null>(null);
  const [error, setError] = useState<string | null>(null);
  // The gate's outcome lives HERE, not in the buttons: approving moves the batch out of
  // the gated state, so the gate — and any message inside it — unmounts on the refresh.
  const [decision, setDecision] = useState<VerbOutcome | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);
  const fileInput = useRef<HTMLInputElement>(null);
  const [uploadInto, setUploadInto] = useState<string>("");
  const [file, setFile] = useState<File | null>(null);

  const refresh = () => setRefreshToken((token) => token + 1);

  /** A gate decision: keep the message, then re-read the batch it changed. */
  const settle = (outcome: VerbOutcome) => {
    setDecision(outcome);
    refresh();
  };

  useEffect(() => {
    let live = true;
    client
      .bulkDefinitions()
      .then((found) => {
        if (!live) return;
        setDefinitions(found);
        setUploadInto((current) => current || found[0]?.name || "");
      })
      .catch(() => live && setError("the bulk definitions could not be loaded"));
    return () => {
      live = false;
    };
  }, [client, refreshToken]);

  const loadBatches = useCallback(
    async (_cursor: string | null, take: number) => {
      // The batch list is take-bounded, not cursor-paged (frozen contract): one page,
      // honestly ended — the table stops instead of faking a cursor it was never given.
      const batches = await client.bulkBatches({ state: state || undefined, take });
      return { items: batches, nextCursor: null };
    },
    [client, state, refreshToken],
  );

  // The validation report IS keyset-paged: the cursor is the last row number of the page.
  const loadErrors = useCallback(
    async (cursor: string | null, take: number) => {
      if (!selectedId) return { items: [] as BulkRowError[], nextCursor: null };
      const errors = await client.bulkErrors(selectedId, { afterRow: cursor ? Number(cursor) : 0, take });
      const last = errors.at(-1);
      // A short page is the end; a full page hands back the last row number as the cursor.
      return { items: errors, nextCursor: errors.length < take || !last ? null : String(last.rowNumber) };
    },
    [client, selectedId, refreshToken],
  );

  // The open batch RE-FETCHES on refresh rather than closing: a gate outcome renders
  // inside this panel, so tearing it down would hide the message just produced.
  useEffect(() => {
    setDecision(null);   // a decision belongs to the batch it was made on
  }, [selectedId]);

  useEffect(() => {
    if (!selectedId) {
      setSelected(null);
      return;
    }

    let live = true;
    client
      .batch(selectedId)
      .then((batch) => live && setSelected(batch))
      .catch(() => live && setError(`batch ${selectedId} could not be opened`));
    return () => {
      live = false;
    };
  }, [client, selectedId, refreshToken]);

  // Upload is the one intake verb that is NOT the admin envelope (it answers the batch it
  // created), so the outcome is built here — honestly: a failed post must not read "ok".
  const upload = async (): Promise<VerbOutcome> => {
    if (!file || !uploadInto) return { kind: "error", status: 0 };
    try {
      const batch = await client.uploadBatch(uploadInto, file);
      // The chosen file is deliberately NOT cleared here: the upload button renders only
      // while a file is selected, so clearing it would unmount the very button whose
      // banner carries the outcome (the U2 gate's teardown lesson, found again here).
      setSelectedId(batch.id);
      refresh();
      return { kind: "ok", message: `${batch.state} — batch ${batch.id} is queued for validation` };
    } catch {
      return { kind: "error", status: 0 };
    }
  };

  if (definitions === null && !error) {
    return <p className="text-sm text-muted-foreground">Loading bulk definitions…</p>;
  }

  const waiting = (definitions ?? []).filter((definition) => definition.awaitingApproval > 0);

  return (
    <div data-testid="bulk-panel" className="space-y-6">
      {error && <Banner tone="danger">{error}</Banner>}

      {waiting.length > 0 && (
        <Banner tone="warning" live="status">
          {waiting
            .map((definition) => {
              const age = definition.oldestAwaitingApprovalSeconds;
              return `${definition.name}: ${definition.awaitingApproval} awaiting approval${age ? ` (oldest ${humanizeSeconds(age)})` : ""}`;
            })
            .join(" · ")}
        </Banner>
      )}

      <section>
        <h2 className="section-title">Definitions</h2>
        <Table
          columns={[
            { header: "Definition", cell: (definition) => <span className="font-medium">{definition.name}</span> },
            {
              header: "Batches by state",
              cell: (definition) => (
                <span className="flex flex-wrap gap-2">
                  {Object.entries(definition.batchesByState).map(([batchState, count]) => (
                    <span key={batchState} className="text-xs text-faint">{batchState}: {count}</span>
                  ))}
                  {Object.keys(definition.batchesByState ?? {}).length === 0 && (
                    <span className="text-xs text-faint">no batches yet</span>
                  )}
                </span>
              ),
            },
          ]}
          rows={definitions ?? []}
          rowKey={(definition) => definition.name}
          emptyMessage="No bulk definition is composed here."
        />
      </section>

      <section>
        <h2 className="section-title">Upload</h2>
        <div className="flex flex-wrap items-center gap-2">
          <label className="text-xs text-muted-foreground" htmlFor="bulk-definition">
            Definition
          </label>
          <select
            id="bulk-definition"
            className="control"
            value={uploadInto}
            onChange={(event) => setUploadInto(event.target.value)}
          >
            {(definitions ?? []).map((definition) => (
              <option key={definition.name} value={definition.name}>
                {definition.name}
              </option>
            ))}
          </select>
          <input
            ref={fileInput}
            type="file"
            aria-label="batch file"
            // The native control is kept (it is the accessible one); only its button half
            // is dressed in the kit's language so it does not read as a foreign widget.
            className="max-w-xs text-sm text-muted-foreground file:mr-3 file:rounded-md file:border file:border-border file:bg-background file:px-3 file:py-1.5 file:text-sm file:font-medium file:text-foreground hover:file:bg-accent"
            onChange={(event) => setFile(event.target.files?.[0] ?? null)}
          />
          {file && (
            <VerbButton
              label="upload"
              confirm={`Upload ${file.name} into ${uploadInto}?`}
              execute={upload}
            />
          )}
        </div>
        <p className="mt-1 text-xs text-faint">
          The file is posted as a raw body, exactly as <code>curl --data-binary</code> would — the console adds no
          format the API does not already accept.
        </p>
      </section>

      <section data-testid="batches">
        <h2 className="section-title">Batches</h2>
        <div className="mb-2 flex flex-wrap items-center gap-2">
          <label className="text-xs text-muted-foreground" htmlFor="bulk-state">
            State
          </label>
          <select
            id="bulk-state"
            className="control"
            value={state}
            onChange={(event) => {
              setState(event.target.value);
              setSelectedId(null);
            }}
          >
            <option value="">all states</option>
            {STATES.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </div>

        <KeysetTable<BulkBatchInfo>
          key={`${state}-${refreshToken}`}
          columns={[
            {
              header: "Batch",
              cell: (batch) => (
                <button className="font-mono text-xs underline-offset-2 hover:underline" onClick={() => setSelectedId(batch.id)}>
                  {batch.id}
                </button>
              ),
            },
            { header: "Definition", cell: (batch) => batch.definition },
            { header: "State", cell: (batch) => <StateBadge state={batch.state} /> },
            { header: "Rows", align: "right", cell: (batch) => `${batch.validRows}/${batch.totalRows}` },
            { header: "Invalid", align: "right", cell: (batch) => batch.invalidRows },
          ]}
          loadPage={loadBatches}
          rowKey={(batch) => batch.id}
          emptyMessage="No batches in this state."
        />
      </section>

      <Sheet
        open={selected !== null}
        onOpenChange={(open) => {
          if (!open) setSelectedId(null);
        }}
        title={selected ? `Batch ${selected.id}` : ""}
        description={selected ? `${selected.definition} — the validation report and the four-eyes gate.` : undefined}
      >
      {selected && (
        <section data-testid="batch-detail">
          <div className="mb-3 flex flex-wrap items-center gap-3">
            <h2 className="text-sm font-medium">
              Batch {selected.id} · {selected.definition}
            </h2>
            <StateBadge state={selected.state} />
            {selected.tenant && <span className="text-xs text-faint">tenant {selected.tenant}</span>}
            {selected.runId && <span className="text-xs text-faint">run {selected.runId}</span>}
            {selected.state === GATED && (
              <span className="ml-auto flex flex-wrap gap-2">
                <VerbButton
                  label="approve"
                  confirm={`Approve ${selected.validRows} valid rows of this batch for execution?`}
                  note={{ label: "note (optional)" }}
                  execute={(note) => asOutcome(client.approveBatch(selected.id, note || undefined))}
                  onDone={settle}
                  quiet
                />
                <VerbButton
                  label="reject"
                  confirm="Reject this batch? Nothing will execute."
                  // The contract makes the note MANDATORY here — it is the gate's evidence.
                  note={{ label: "reason (required)", required: true }}
                  execute={(note) => asOutcome(client.rejectBatch(selected.id, note ?? ""))}
                  onDone={settle}
                  quiet
                  destructive
                />
              </span>
            )}
          </div>

          {decision && decision.kind !== "error" && (
            // The engine's own words, verbatim — a refusal here teaches the fix
            // ("invalid rows block approval…"), so it must never be paraphrased.
            <Banner tone={decision.kind === "ok" ? "success" : "danger"} live={decision.kind === "ok" ? "status" : "alert"}>
              {decision.message}
            </Banner>
          )}

          {decision?.kind === "error" && (
            <Banner tone="warning">the decision did not reach the server — it may not have been recorded</Banner>
          )}

          <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-1 text-xs sm:grid-cols-4">
            <div>
              <dt className="text-faint">Total rows</dt>
              <dd>{selected.totalRows}</dd>
            </div>
            <div>
              <dt className="text-faint">Valid</dt>
              <dd>{selected.validRows}</dd>
            </div>
            <div>
              <dt className="text-faint">Invalid</dt>
              <dd className={selected.invalidRows > 0 ? "text-danger" : undefined}>{selected.invalidRows}</dd>
            </div>
            <div>
              <dt className="text-faint">Executed / failed</dt>
              <dd>
                {selected.executedRows} / <span className={selected.failedRows > 0 ? "text-danger" : undefined}>{selected.failedRows}</span>
              </dd>
            </div>
          </dl>

          {selected.decidedAt && (
            // The decision evidence, as the server recorded it — the actor comes from the
            // token, never from this screen.
            <p className="mt-3 text-xs text-muted-foreground">
              {selected.state === "Rejected" ? "Rejected" : "Approved"} by {selected.decidedBy ?? "unknown"} at{" "}
              {selected.decidedAt}
              {selected.decisionNote ? ` — “${selected.decisionNote}”` : ""}
            </p>
          )}

          <h3 className="control-label mb-1 mt-4 block">
            Validation report{selected.invalidRows === 0 ? " — no findings" : ""}
          </h3>
          {selected.invalidRows > 0 && (
            <KeysetTable<BulkRowError>
              key={`${selected.id}-${refreshToken}`}
              columns={[
                { header: "Row", align: "right", cell: (row) => (row.rowNumber === 0 ? "file" : row.rowNumber) },
                { header: "Field", cell: (row) => row.field },
                { header: "Finding", cell: (row) => row.message },
              ]}
              loadPage={loadErrors}
              rowKey={(row) => String(row.id)}
              take={100}
              emptyMessage="No findings recorded for this batch."
            />
          )}
        </section>
      )}
      </Sheet>
    </div>
  );
}

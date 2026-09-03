import { RefreshCw } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Banner, DensityToggle, DetailSection, FacetFilter, IconAction, KeyValueRows, KeysetTable, Sheet, StateBadge, Table } from "@qorpe/ui";
import type { AdminClient, FileInfo, FileQuarantineInfo, FileRailInfo } from "./adminClient";

export interface FileExchangePanelProps {
  client: AdminClient;
}

/**
 * The file-rail panel (console federation — T22). The surface is READ-ONLY by contract and
 * so is this screen: a file arrives through its transport and pick-up job, and re-delivering
 * it IS the reprocess (the engine dedups on the (rail, file, line) key by construction) —
 * a console that could "reprocess" without the file's bytes would only pretend. What the
 * operator gets here is the rail's story: files in, rows applied, rows quarantined with
 * their reason and their age.
 */
export function FileExchangePanel({ client }: FileExchangePanelProps) {
  const [rails, setRails] = useState<FileRailInfo[] | null>(null);
  const [railFilter, setRailFilter] = useState<string[]>([]);
  const [selectedFile, setSelectedFile] = useState<FileInfo | null>(null);
  const [quarantine, setQuarantine] = useState<FileQuarantineInfo[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  const refresh = () => setRefreshToken((token) => token + 1);

  useEffect(() => {
    let live = true;
    client
      .fileRails()
      .then((found) => live && setRails(found))
      .catch(() => live && setError("the file rails could not be loaded"));
    return () => {
      live = false;
    };
  }, [client, refreshToken]);

  const loadRows = useCallback(
    async (_cursor: string | null, take: number) => {
      const rows = await client.files({ rail: railFilter.length > 0 ? railFilter : undefined, take });
      return { items: rows, nextCursor: null };
    },
    [client, railFilter, refreshToken],
  );

  // The open file RE-READS its quarantine: a quarantined row is a fix in MOTION (the
  // counterparty resends, the row applies, the record clears), so the count captured in
  // the list is not what the operator should judge.
  useEffect(() => {
    if (!selectedFile) {
      setQuarantine(null);
      return;
    }

    let live = true;
    client
      .fileQuarantine({ rail: [selectedFile.rail], file: [selectedFile.file], take: 500 })
      .then((found) => live && setQuarantine(found))
      .catch(() => live && setError(`the quarantine of ${selectedFile.file} could not be read`));
    return () => {
      live = false;
    };
  }, [client, selectedFile, refreshToken]);

  const inQuarantine = (rails ?? []).filter((rail) => rail.quarantineDepth > 0);

  return (
    <div data-testid="fileexchange-panel" className="space-y-6">
      {error && <Banner tone="danger">{error}</Banner>}

      {inQuarantine.length > 0 && (
        <Banner tone="warning" live="status">
          {inQuarantine.map((rail) => `${rail.name}: ${rail.quarantineDepth} row${rail.quarantineDepth === 1 ? "" : "s"} in quarantine`).join(" · ")}
        </Banner>
      )}

      <section data-testid="rails">
        <h2 className="section-title">Rails</h2>
        <Table
          columns={[
            { header: "Rail", cell: (rail) => <span className="font-medium">{rail.name}</span> },
            { header: "Header lines", align: "right", cell: (rail) => rail.headerLines },
            { header: "Files archived", align: "right", cell: (rail) => rail.filesArchived },
            {
              header: "In quarantine",
              align: "right",
              cell: (rail) => <span className={rail.quarantineDepth > 0 ? "font-semibold text-warning" : undefined}>{rail.quarantineDepth}</span>,
            },
            { header: "Last archived", cell: (rail) => <span className="font-mono text-xs">{rail.lastArchivedAt ?? "—"}</span> },
          ]}
          rows={rails ?? []}
          rowKey={(rail) => rail.name}
          emptyMessage="No rails are declared in this app."
        />
      </section>

      <section data-testid="files">
        <KeysetTable<FileInfo>
          toolbar={
            <>
              <FacetFilter
                label="Rail"
                options={(rails ?? []).map((rail) => ({ value: rail.name }))}
                selected={new Set(railFilter)}
                onToggle={(value) => {
                  setRailFilter((current) => (current.includes(value) ? current.filter((v) => v !== value) : [...current, value]));
                  setSelectedFile(null);
                }}
                onClear={() => {
                  setRailFilter([]);
                  setSelectedFile(null);
                }}
              />
              <span className="ml-auto flex items-center gap-2">
                <IconAction icon={<RefreshCw />} label="Refresh" onClick={refresh} />
                <DensityToggle />
              </span>
            </>
          }
          columns={[
            {
              header: "File",
              cell: (row) => (
                <button className="font-mono text-xs underline-offset-2 hover:underline" onClick={() => setSelectedFile(row)}>
                  {row.file}
                </button>
              ),
            },
            { header: "Rail", cell: (row) => row.rail },
            { header: "Rows applied", align: "right", cell: (row) => row.processedRows },
            {
              header: "In quarantine",
              align: "right",
              cell: (row) => <span className={row.quarantinedRows > 0 ? "font-semibold text-warning" : undefined}>{row.quarantinedRows}</span>,
            },
            { header: "State", cell: (row) => <StateBadge state={row.archived ? "Archived" : "InFlight"} /> },
            { header: "Archived", cell: (row) => <span className="font-mono text-xs">{row.archivedAt ?? "—"}</span> },
          ]}
          loadPage={loadRows}
          rowKey={(row) => `${row.rail}/${row.file}`}
          emptyMessage="No files have arrived on these rails yet."
        />
        <p className="mt-1 text-xs text-faint">
          Re-delivering a file IS the reprocess: applied rows dedup on the (rail, file, line) key, fixed rows apply and their
          quarantine records clear. This surface has no verbs — nothing here can invent a file's bytes.
        </p>
      </section>

      <Sheet
        open={selectedFile !== null}
        onOpenChange={(open) => {
          if (!open) setSelectedFile(null);
        }}
        title={selectedFile ? `${selectedFile.rail} · ${selectedFile.file}` : ""}
        description={selectedFile ? "The file's run: what applied, and every quarantined row with its reason and age." : undefined}
      >
        {selectedFile && (
          <section data-testid="file-detail">
            <div className="mb-3 flex flex-wrap items-center gap-3">
              <StateBadge state={selectedFile.archived ? "Archived" : "InFlight"} />
              <IconAction icon={<RefreshCw />} label="Refresh" onClick={refresh} />
            </div>

            <DetailSection title="Run">
              <KeyValueRows
                rows={[
                  { key: "Rows applied", value: String(selectedFile.processedRows), mono: true },
                  { key: "In quarantine", value: String(selectedFile.quarantinedRows), mono: true },
                  { key: "Archived", value: selectedFile.archivedAt ?? "not yet", mono: true },
                ]}
              />
            </DetailSection>

            <DetailSection title="Quarantine">
              {quarantine === null ? (
                <p className="text-sm text-muted-foreground">Reading the quarantine…</p>
              ) : (
                <Table
                  columns={[
                    { header: "Line", align: "right", cell: (row) => <span className="font-mono text-xs">{row.line}</span> },
                    // The server's own words: the parse throw, the row-contract reason, or the handler's failure.
                    { header: "Reason", cell: (row) => <span className="text-danger">{row.reason}</span> },
                    { header: "Since", cell: (row) => <span className="font-mono text-xs">{row.quarantinedAt}</span> },
                  ]}
                  rows={quarantine}
                  rowKey={(row) => String(row.line)}
                  emptyMessage="Nothing is quarantined on this file — every row applied."
                />
              )}
            </DetailSection>
          </section>
        )}
      </Sheet>
    </div>
  );
}

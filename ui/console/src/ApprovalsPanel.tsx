import { RefreshCw } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Banner, Button, DensityToggle, DetailSection, FacetFilter, IconAction, Input, KeyValueRows, KeysetTable, Sheet, StateBadge } from "@qorpe/ui";
import type { AdminClient, ApprovalRequestDetail, ApprovalRequestInfo } from "./adminClient";

export interface ApprovalsPanelProps {
  client: AdminClient;
}

/** The request lifecycle, in its own order. */
const STATUSES = ["Pending", "Granted", "Rejected", "Expired", "Withdrawn"] as const;

/**
 * The approvals panel (console federation — T21): the worklist the ladder engine keeps,
 * with the quorum said as a number (2/2, never a mystery) and the trail as the story.
 * Deciding here goes through the ENGINE unchanged — four-eyes, distinct-eyes, role and
 * the mandatory rejection reason all hold; a refusal comes back as the RULE's name and is
 * shown verbatim.
 */
export function ApprovalsPanel({ client }: ApprovalsPanelProps) {
  const [statusFilter, setStatusFilter] = useState<string[]>(["Pending"]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selected, setSelected] = useState<ApprovalRequestDetail | null>(null);
  const [role, setRole] = useState("");
  const [reason, setReason] = useState("");
  const [verbMessage, setVerbMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  const refresh = () => setRefreshToken((token) => token + 1);

  const loadRows = useCallback(
    async (_cursor: string | null, take: number) => {
      const rows = await client.approvals({ status: statusFilter.length > 0 ? statusFilter : undefined, take });
      return { items: rows, nextCursor: null };
    },
    [client, statusFilter, refreshToken],
  );

  // The open row RE-READS by id: a pending request is a decision in MOTION (signatures
  // arrive, rungs escalate) — the row captured minutes ago is not what a decider signs.
  useEffect(() => {
    if (!selectedId) {
      setSelected(null);
      setVerbMessage(null);
      return;
    }

    let live = true;
    client
      .approval(selectedId)
      .then((found) => {
        if (!live) return;
        setSelected(found);
        setRole(found.request.pendingRole);
      })
      .catch(() => live && setError(`approval ${selectedId} could not be read`));
    return () => {
      live = false;
    };
  }, [client, selectedId, refreshToken]);

  const decide = async (granted: boolean) => {
    if (!selectedId) return;
    setVerbMessage(null);
    const result = granted
      ? await client.approveApproval(selectedId, role, reason || undefined)
      : await client.rejectApproval(selectedId, role, reason);
    // The envelope speaks either way: Applied, or the rule that refused (FourEyesViolation,
    // ReasonRequired, …) — shown verbatim, because the rule's name IS the explanation.
    setVerbMessage(result.message);
    if (result.ok) {
      setReason("");
      refresh();
    }
  };

  const quorum = (row: ApprovalRequestInfo) =>
    row.status === "Pending" && row.requiredApprovals > 1 ? `${row.signatureCount}/${row.requiredApprovals}` : "—";

  return (
    <div data-testid="approvals-panel" className="space-y-6">
      {error && <Banner tone="danger">{error}</Banner>}

      <section data-testid="approvals-queue">
        <KeysetTable<ApprovalRequestInfo>
          toolbar={
            <>
              <FacetFilter
                label="Status"
                options={STATUSES.map((option) => ({ value: option }))}
                selected={new Set(statusFilter)}
                onToggle={(value) => {
                  setStatusFilter((current) => (current.includes(value) ? current.filter((v) => v !== value) : [...current, value]));
                  setSelectedId(null);
                }}
                onClear={() => {
                  setStatusFilter([]);
                  setSelectedId(null);
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
              header: "Subject",
              cell: (row) => (
                <button className="font-medium underline-offset-2 hover:underline" onClick={() => setSelectedId(row.id)}>
                  {row.subject}
                </button>
              ),
            },
            { header: "Ladder", cell: (row) => row.ladder },
            { header: "Amount", align: "right", cell: (row) => <span className="font-mono text-xs">{row.amount}</span> },
            { header: "Pending role", cell: (row) => (row.status === "Pending" ? row.pendingRole : "—") },
            { header: "Quorum", cell: (row) => <span className="font-mono text-xs">{quorum(row)}</span> },
            { header: "Requested by", cell: (row) => row.requestedBy },
            { header: "Status", cell: (row) => <StateBadge state={row.status} /> },
            { header: "Requested", cell: (row) => row.requestedAt },
          ]}
          loadPage={loadRows}
          rowKey={(row) => row.id}
          emptyMessage="Nothing is waiting — every request on this ladder set has been decided."
        />
        <p className="mt-1 text-xs text-faint">
          Deciding here runs the SAME engine every other decider faces: four-eyes, distinct-eyes across the chain,
          the rung's role, and a mandatory reason on rejection. A refusal names its rule.
        </p>
      </section>

      <Sheet
        open={selected !== null}
        onOpenChange={(open) => {
          if (!open) setSelectedId(null);
        }}
        title={selected ? `${selected.request.ladder} · ${selected.request.subject}` : ""}
        description={selected ? "The request's full story: routing, signatures, and the audited trail." : undefined}
      >
        {selected && (
          <section data-testid="approval-detail">
            <div className="mb-3 flex flex-wrap items-center gap-3">
              <StateBadge state={selected.request.status} />
              <IconAction icon={<RefreshCw />} label="Refresh" onClick={refresh} />
            </div>

            <DetailSection title="Request">
              <KeyValueRows
                rows={[
                  { key: "Amount", value: String(selected.request.amount), mono: true },
                  { key: "Requested by", value: selected.request.requestedBy },
                  { key: "Requested at", value: selected.request.requestedAt, mono: true },
                  ...(selected.request.status === "Pending"
                    ? [
                        { key: "Pending role", value: selected.request.pendingRole },
                        { key: "Quorum", value: `${selected.request.signatureCount}/${selected.request.requiredApprovals}`, mono: true },
                      ]
                    : [
                        { key: "Decided by", value: selected.request.decidedBy ?? "—" },
                        { key: "Reason", value: selected.request.reason ?? "—" },
                      ]),
                  ...(selected.request.supersedesId
                    ? [{ key: "Supersedes", value: selected.request.supersedesId, mono: true }]
                    : []),
                ]}
              />
            </DetailSection>

            {selected.signatures.length > 0 && (
              <DetailSection title="Signatures">
                <KeyValueRows
                  rows={selected.signatures.map((signature) => ({
                    key: signature.signedBy,
                    value: `${signature.role} · ${signature.at}`,
                    mono: true,
                  }))}
                />
              </DetailSection>
            )}

            <DetailSection title="Trail">
              <KeyValueRows
                rows={selected.trail.map((entry, index) => ({
                  key: `${index + 1}. ${entry.action}`,
                  value: `${entry.actor} — ${entry.detail}`,
                }))}
              />
            </DetailSection>

            {selected.request.status === "Pending" && (
              <DetailSection title="Decide">
                <div className="space-y-2">
                  <Input aria-label="Decider role" value={role} onChange={(event) => setRole(event.target.value)} placeholder="role (the rung's, or a delegated one)" />
                  <Input aria-label="Reason" value={reason} onChange={(event) => setReason(event.target.value)} placeholder="reason — mandatory on reject" />
                  <div className="flex gap-2">
                    <Button onClick={() => decide(true)}>Approve</Button>
                    <Button variant="outline" onClick={() => decide(false)}>Reject</Button>
                  </div>
                </div>
              </DetailSection>
            )}

            {verbMessage && (
              <p data-testid="verb-message" className={`mt-3 text-xs ${verbMessage === "Applied" ? "text-muted-foreground" : "text-danger"}`}>
                {verbMessage}
              </p>
            )}
          </section>
        )}
      </Sheet>
    </div>
  );
}

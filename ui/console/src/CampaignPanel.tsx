import { Pause, Play, Square } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Banner, Checkbox, FacetFilter, KeysetTable, Sheet, StateBadge, VerbButton, humanizeSeconds } from "@goldpath/kit";
import type { VerbOutcome } from "@goldpath/kit";
import type {
  AdminClient,
  CampaignAuditEntry,
  CampaignFailedItem,
  CampaignInfo,
  CampaignThrottle,
} from "./adminClient";

export interface CampaignPanelProps {
  client: AdminClient;
}

/** The verb envelope, adapted to the kit's outcome type — refusals stay data. */
async function asOutcome(call: Promise<{ ok: boolean; message: string }>): Promise<VerbOutcome> {
  const result = await call;
  return result.ok ? { kind: "ok", message: result.message } : { kind: "refused", message: result.message };
}

/** The pacer's state machine, in its own order. */
const STATES = ["Created", "Enumerating", "Running", "Paused", "Completed", "CompletedWithFailures", "Aborted"] as const;

/** Only a live campaign can be paused, resumed or aborted; the rest already ended. */
const LIVE = new Set(["Created", "Enumerating", "Running"]);

/** The throttle form's raw text; empty means "leave this as it is" (the patch's null). */
interface ThrottleDraft {
  tps: string;
  dailyQuota: string;
  maxInFlight: string;
  windowStart: string;
  windowEnd: string;
  timeZoneId: string;
  clearDailyQuota: boolean;
  clearWindow: boolean;
}

const EMPTY_DRAFT: ThrottleDraft = {
  tps: "",
  dailyQuota: "",
  maxInFlight: "",
  windowStart: "",
  windowEnd: "",
  timeZoneId: "",
  clearDailyQuota: false,
  clearWindow: false,
};

/**
 * Turns the draft into the contract's patch: an untouched field is simply ABSENT, so the
 * server keeps its current value. Clearing is explicit on purpose — "no daily quota"
 * cannot be expressed by leaving a box empty.
 */
export function toThrottle(draft: ThrottleDraft): CampaignThrottle {
  const patch: CampaignThrottle = {};
  const number = (raw: string) => (raw.trim() === "" ? undefined : Number(raw));
  const text = (raw: string) => (raw.trim() === "" ? undefined : raw.trim());
  if (number(draft.tps) !== undefined) patch.tps = number(draft.tps);
  if (number(draft.dailyQuota) !== undefined) patch.dailyQuota = number(draft.dailyQuota);
  if (number(draft.maxInFlight) !== undefined) patch.maxInFlight = number(draft.maxInFlight);
  if (text(draft.windowStart)) patch.windowStart = text(draft.windowStart);
  if (text(draft.windowEnd)) patch.windowEnd = text(draft.windowEnd);
  if (text(draft.timeZoneId)) patch.timeZoneId = text(draft.timeZoneId);
  if (draft.clearDailyQuota) patch.clearDailyQuota = true;
  if (draft.clearWindow) patch.clearWindow = true;
  return patch;
}

/** The confirm line: what the operator is actually about to change, field by field. */
export function describeThrottle(patch: CampaignThrottle): string {
  const parts: string[] = [];
  if (patch.tps !== undefined) parts.push(`${patch.tps} tps`);
  if (patch.dailyQuota !== undefined) parts.push(`daily quota ${patch.dailyQuota}`);
  if (patch.maxInFlight !== undefined) parts.push(`max in-flight ${patch.maxInFlight}`);
  if (patch.windowStart || patch.windowEnd) parts.push(`window ${patch.windowStart ?? "…"}–${patch.windowEnd ?? "…"}`);
  if (patch.timeZoneId) parts.push(`time zone ${patch.timeZoneId}`);
  if (patch.clearDailyQuota) parts.push("no daily quota");
  if (patch.clearWindow) parts.push("no window (release around the clock)");
  return parts.join(", ");
}

/**
 * The campaign governor (console RFC §3): the pacer's numbers, the policy in force, and
 * the live verbs — pause, resume, abort, and a throttle that takes effect on the next
 * tick. Item REPLAY is deliberately absent: repair has ONE home, the run console.
 */
export function CampaignPanel({ client }: CampaignPanelProps) {
  const [state, setState] = useState<string>("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selected, setSelected] = useState<CampaignInfo | null>(null);
  const [failures, setFailures] = useState<CampaignFailedItem[]>([]);
  const [audit, setAudit] = useState<CampaignAuditEntry[]>([]);
  const [draft, setDraft] = useState<ThrottleDraft>(EMPTY_DRAFT);
  const [error, setError] = useState<string | null>(null);
  const [ready, setReady] = useState(false);
  const [refreshToken, setRefreshToken] = useState(0);
  // The verb outcome lives HERE: pausing or aborting moves the campaign out of the live
  // set, so the buttons — and any message inside them — unmount on the refresh.
  const [outcome, setOutcome] = useState<VerbOutcome | null>(null);

  const refresh = () => setRefreshToken((token) => token + 1);
  const settle = (result: VerbOutcome) => {
    setOutcome(result);
    refresh();
  };

  const loadCampaigns = useCallback(
    async (_cursor: string | null, take: number) => {
      // Take-bounded, not cursor-paged (frozen contract): one page, honestly ended.
      const campaigns = await client.campaigns({ state: state || undefined, take });
      setReady(true);
      return { items: campaigns, nextCursor: null };
    },
    [client, state, refreshToken],
  );

  useEffect(() => {
    setOutcome(null);   // a verb's answer belongs to the campaign it was aimed at
    setDraft(EMPTY_DRAFT);
  }, [selectedId]);

  // The open campaign RE-FETCHES on refresh instead of closing — a governor watches
  // numbers move, and the panel carries the outcome strip.
  useEffect(() => {
    if (!selectedId) {
      setSelected(null);
      setFailures([]);
      setAudit([]);
      return;
    }

    let live = true;
    Promise.all([client.campaign(selectedId), client.campaignFailures(selectedId), client.campaignAudit(selectedId)])
      .then(([info, failed, entries]) => {
        if (!live) return;
        setSelected(info);
        setFailures(failed);
        setAudit(entries);
      })
      .catch(() => live && setError(`campaign ${selectedId} could not be opened`));
    return () => {
      live = false;
    };
  }, [client, selectedId, refreshToken]);

  const patch = toThrottle(draft);
  const patched = Object.keys(patch).length > 0;

  return (
    <div data-testid="campaign-panel" className="space-y-6">
      {error && <Banner tone="danger">{error}</Banner>}

      <section>
        <div className="mb-2 flex flex-wrap items-center gap-2">
          <h2 className="text-sm font-medium text-muted-foreground">Campaigns</h2>
          {/*
            The family facet (§8.14). It COMMITS one state: the frozen contract's ?state=
            takes a single value until revision R3 lands — toggling the active one clears.
          */}
          <FacetFilter
            label="State"
            options={STATES.map((option) => ({ value: option }))}
            selected={new Set(state ? [state] : [])}
            onToggle={(value) => {
              setState(value === state ? "" : value);
              setSelectedId(null);
            }}
            onClear={() => {
              setState("");
              setSelectedId(null);
            }}
          />
          {/*
            A governor watches numbers that move under it: without a re-read the table
            and the open campaign drift apart until a verb happens to refresh them.
          */}
          <button
            className="btn-quiet ml-auto"
            onClick={refresh}
          >
            refresh
          </button>
        </div>

        <KeysetTable<CampaignInfo>
          key={`${state}-${refreshToken}`}
          columns={[
            {
              header: "Campaign",
              cell: (campaign) => (
                <button className="text-xs underline-offset-2 hover:underline" onClick={() => setSelectedId(campaign.id)}>
                  {campaign.name}
                </button>
              ),
            },
            { header: "Type", cell: (campaign) => campaign.type },
            { header: "State", cell: (campaign) => <StateBadge state={campaign.state} /> },
            { header: "Released", align: "right", cell: (campaign) => campaign.releasedThrough },
            { header: "Remaining", align: "right", cell: (campaign) => campaign.remaining },
            { header: "Failed", align: "right", cell: (campaign) => campaign.failedCount },
            { header: "tps", align: "right", cell: (campaign) => campaign.tps },
          ]}
          loadPage={loadCampaigns}
          rowKey={(campaign) => campaign.id}
          emptyMessage={ready ? "No campaigns in this state." : "Loading campaigns…"}
        />
      </section>

      {/* The governor opens in the right Sheet (§8.14) — the inline-below card retires. */}
      <Sheet
        open={selected !== null}
        onOpenChange={(open) => {
          if (!open) setSelectedId(null);
        }}
        title={selected ? `${selected.name} · ${selected.type}` : ""}
        description={selected ? "Release policy, progress, failures, and the audit trail." : undefined}
      >
      {selected && (
        <section data-testid="campaign-detail">
          <div className="mb-3 flex flex-wrap items-center gap-3">
            <StateBadge state={selected.state} />
            {/* The governor watches numbers that MOVE — and the list's refresh sits
                behind this modal sheet, so the sheet carries its own. */}
            <button className="btn-quiet" onClick={refresh}>refresh</button>
            {!selected.windowOpenNow && (LIVE.has(selected.state) || selected.state === "Paused") && (
              // Nothing is wrong — the policy simply says "not now".
              <span className="text-xs text-warning">outside the release window</span>
            )}
            {LIVE.has(selected.state) && (
              <span className="ml-auto flex flex-wrap gap-2">
                <VerbButton
                  label="pause"
                  icon={<Pause />}
                  iconOnly
                  confirm={`Pause ${selected.name}? In-flight items drain; nothing new is released.`}
                  execute={() => asOutcome(client.pauseCampaign(selected.id))}
                  onDone={settle}
                  quiet
                />
                <VerbButton
                  label="abort"
                  icon={<Square />}
                  iconOnly
                  confirm={`Abort ${selected.name}? The ${selected.remaining} remaining items are stamped Aborted — this cannot be undone.`}
                  note={{ label: "reason (required)", required: true }}
                  execute={(reason) => asOutcome(client.abortCampaign(selected.id, reason ?? ""))}
                  onDone={settle}
                  quiet
                  destructive
                />
              </span>
            )}
            {selected.state === "Paused" && (
              <span className="ml-auto flex flex-wrap gap-2">
                <VerbButton
                  label="resume"
                  icon={<Play />}
                  iconOnly
                  confirm={`Resume ${selected.name}? Release continues under the policy below.`}
                  execute={() => asOutcome(client.resumeCampaign(selected.id))}
                  onDone={settle}
                  quiet
                />
                <VerbButton
                  label="abort"
                  confirm={`Abort ${selected.name}? The ${selected.remaining} remaining items are stamped Aborted — this cannot be undone.`}
                  note={{ label: "reason (required)", required: true }}
                  execute={(reason) => asOutcome(client.abortCampaign(selected.id, reason ?? ""))}
                  onDone={settle}
                  quiet
                  destructive
                />
              </span>
            )}
          </div>

          {outcome && outcome.kind !== "error" && (
            // The pacer's own words — a refusal here explains which state it is in.
            <Banner tone={outcome.kind === "ok" ? "success" : "danger"} live={outcome.kind === "ok" ? "status" : "alert"}>
              {outcome.message}
            </Banner>
          )}
          {outcome?.kind === "error" && (
            <Banner tone="warning">the verb did not reach the server — it may not have been applied</Banner>
          )}

          <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 text-xs sm:grid-cols-4">
            <div>
              <dt className="text-faint">Released</dt>
              <dd>
                {selected.releasedThrough} of {selected.enumeratedThrough}
                {!selected.enumerationComplete && <span className="text-faint"> (still enumerating)</span>}
              </dd>
            </div>
            <div>
              <dt className="text-faint">Succeeded / failed</dt>
              <dd>
                {selected.succeededCount} /{" "}
                <span className={selected.failedCount > 0 ? "text-danger" : undefined}>{selected.failedCount}</span>
              </dd>
            </div>
            <div>
              <dt className="text-faint">In flight</dt>
              <dd>
                {selected.inFlight} of {selected.maxInFlight} allowed
              </dd>
            </div>
            <div>
              <dt className="text-faint">Remaining</dt>
              <dd>
                {selected.remaining}
                {selected.etaSecondsAtCurrentTps != null && (
                  // Honest label: this is the arithmetic at the CURRENT rate, not a promise.
                  <span className="text-faint"> · ~{humanizeSeconds(selected.etaSecondsAtCurrentTps)} at {selected.tps} tps</span>
                )}
              </dd>
            </div>
            <div>
              <dt className="text-faint">Daily quota</dt>
              <dd>
                {selected.dailyQuota == null ? "none" : `${selected.releasedToday} of ${selected.dailyQuota} today`}
              </dd>
            </div>
            <div>
              <dt className="text-faint">Window</dt>
              <dd>
                {selected.windowStart && selected.windowEnd
                  ? `${selected.windowStart}–${selected.windowEnd} ${selected.timeZoneId}`
                  : "around the clock"}
                {" · "}
                {selected.windowOpenNow ? "open now" : "closed now"}
              </dd>
            </div>
            <div>
              <dt className="text-faint">Created</dt>
              <dd>
                {selected.createdAt} by {selected.createdBy}
              </dd>
            </div>
            <div>
              <dt className="text-faint">Last verb</dt>
              <dd>{selected.lastVerb ?? "—"}</dd>
            </div>
          </dl>

          {LIVE.has(selected.state) && (
            <div className="row-card mt-4">
              <h3 className="control-label mb-2 block">
                Throttle — takes effect on the next pacer tick; an empty box keeps its current value
              </h3>
              <div className="flex flex-wrap items-end gap-3">
                {(
                  [
                    ["tps", "tps", selected.tps],
                    ["dailyQuota", "daily quota", selected.dailyQuota],
                    ["maxInFlight", "max in-flight", selected.maxInFlight],
                  ] as const
                ).map(([field, label, current]) => (
                  <label key={field} className="flex flex-col gap-1 text-xs text-muted-foreground">
                    {label} <span className="text-faint">(now {current ?? "none"})</span>
                    <input
                      type="number"
                      min={0}
                      aria-label={label}
                      className="control w-28"
                      value={draft[field]}
                      onChange={(event) => setDraft({ ...draft, [field]: event.target.value })}
                    />
                  </label>
                ))}
                {(
                  [
                    ["windowStart", "window start"],
                    ["windowEnd", "window end"],
                  ] as const
                ).map(([field, label]) => (
                  <label key={field} className="flex flex-col gap-1 text-xs text-muted-foreground">
                    {label}
                    <input
                      type="time"
                      aria-label={label}
                      className="control"
                      value={draft[field]}
                      onChange={(event) => setDraft({ ...draft, [field]: event.target.value })}
                    />
                  </label>
                ))}
                <label className="flex flex-col gap-1 text-xs text-muted-foreground">
                  time zone
                  <input
                    type="text"
                    aria-label="time zone"
                    placeholder={selected.timeZoneId}
                    className="control w-40"
                    value={draft.timeZoneId}
                    onChange={(event) => setDraft({ ...draft, timeZoneId: event.target.value })}
                  />
                </label>
                <Checkbox
                  checked={draft.clearDailyQuota}
                  onChange={(clearDailyQuota) => setDraft({ ...draft, clearDailyQuota })}
                  label="clear daily quota"
                />
                <Checkbox
                  checked={draft.clearWindow}
                  onChange={(clearWindow) => setDraft({ ...draft, clearWindow })}
                  label="clear window"
                />
                {patched && (
                  <VerbButton
                    label="throttle"
                    confirm={`Change the release policy of ${selected.name} to ${describeThrottle(patch)}?`}
                    execute={() => asOutcome(client.throttleCampaign(selected.id, patch))}
                    onDone={(result) => {
                      if (result.kind === "ok") setDraft(EMPTY_DRAFT);
                      settle(result);
                    }}
                    quiet
                  />
                )}
              </div>
            </div>
          )}

          <h3 className="control-label mb-1 mt-4 block">
            Failed items{failures.length === 0 ? " — none" : ` (${failures.length} shown)`}
          </h3>
          {failures.length > 0 && (
            <>
              <ul className="space-y-1">
                {failures.map((item) => (
                  <li key={item.seq} className="flex items-baseline gap-2 text-xs">
                    <span className="font-mono">#{item.seq}</span>
                    <span className="text-danger">{item.error ?? "no error recorded"}</span>
                    {item.completedAt && <span className="text-faint">{item.completedAt}</span>}
                  </li>
                ))}
              </ul>
              <p className="mt-1 text-xs text-faint">
                Repair has one home: replay these items from the run console, not from here.
              </p>
            </>
          )}

          <h3 className="control-label mb-1 mt-4 block">Verb log</h3>
          {/*
            NOT the AuditBlock: that composite renders property-level old→new rows of the
            audit-trail module. A campaign's audit is verb-level (who ran what, why), so
            forcing it into that shape would invent fields the server never sent.
          */}
          <ul className="space-y-1">
            {audit.map((entry) => (
              <li key={entry.id} className="flex flex-wrap items-baseline gap-2 text-xs">
                <span className="text-faint">{entry.at}</span>
                <span className="font-medium">{entry.action}</span>
                <span>{entry.actor}</span>
                {entry.detail && <span className="text-muted-foreground">{entry.detail}</span>}
              </li>
            ))}
            {audit.length === 0 && <li className="text-xs text-faint">No verbs recorded yet.</li>}
          </ul>
        </section>
      )}
      </Sheet>
    </div>
  );
}

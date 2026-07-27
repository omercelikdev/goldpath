/**
 * The console's ONLY door to a service: the FROZEN admin contract
 * (docs/rfc/goldpath-admin-contract.md). Every call carries the operator's credentials —
 * the console is a well-dressed client of the same API adopters script, nothing more.
 */
export interface AdminClientOptions {
  /** Service root, e.g. "" for same-origin or "https://payments.internal". */
  baseUrl?: string;
  fetcher?: typeof fetch;
}

/** The five module surfaces of the frozen contract. */
export const MODULES = ["jobs", "archival", "bulk", "notification", "campaign"] as const;
export type ModuleName = (typeof MODULES)[number];

/**
 * The probe route per module — the contract's own list root. A 404 means the module was
 * never composed into this app, so the panel does not exist (console RFC §2: no manifest
 * upload, no config drift — the API is the truth here too).
 */
const PROBE: Record<ModuleName, string> = {
  jobs: "/goldpath/admin/jobs/fleets",
  archival: "/goldpath/admin/archival/definitions",
  bulk: "/goldpath/admin/bulk/definitions",
  notification: "/goldpath/admin/notification/templates",
  campaign: "/goldpath/admin/campaign/",
};

export interface FleetInfo {
  schedulerName: string;
  jobCount: number;
  nodes: { instanceId: string; isClustered?: boolean }[];
}

export interface JobInfo {
  name: string;
  group?: string;
  requestsRecovery?: boolean;
  paused?: boolean;
  nextFireTime?: string | null;
}

export interface RunSummary {
  id: string;
  jobName: string;
  status: string;
  startedAt: string;
  finishedAt?: string | null;
  deadlineAt?: string | null;
  predictedFinishAt?: string | null;
  totalChunks: number;
  completedChunks: number;
  failedChunks: number;
  totalItems?: number | null;
  itemFailures: number;
}

/**
 * The run detail as the contract actually returns it — a NESTED record
 * (`GoldpathRunDetail(Run, ChunksByStatus, OpenFailures)`), not a flattened row:
 * chunks arrive as counts per status, and open failures are capped at 200 server-side.
 */
export interface RunDetail {
  run: RunSummary;
  chunksByStatus: Record<string, number>;
  openFailures: {
    id: number;
    runId: string;
    chunkIndex: number;
    itemKey: string;
    reason: string;
    failedAt: string;
    redrivenAt?: string | null;
  }[];
}

/** Intake numbers per definition — the panel's headline row. */
export interface BulkDefinitionStatus {
  name: string;
  batchesByState: Record<string, number>;
  awaitingApproval: number;
  oldestAwaitingApprovalSeconds?: number | null;
}

/** One batch over the wire: the state machine's public face. */
export interface BulkBatchInfo {
  id: string;
  definition: string;
  state: string;
  tenant?: string | null;
  totalRows: number;
  validRows: number;
  invalidRows: number;
  executedRows: number;
  failedRows: number;
  runId?: string | null;
  receivedAt: string;
  validatedAt?: string | null;
  decidedAt?: string | null;
  decidedBy?: string | null;
  decisionNote?: string | null;
  completedAt?: string | null;
}

/** One validation finding — teaching text, value-free by contract. */
export interface BulkRowError {
  id: number;
  batchId: string;
  rowNumber: number;
  field: string;
  message: string;
}

/** A campaign as the governor sees it: pacer counters + the live policy in force. */
export interface CampaignInfo {
  id: string;
  type: string;
  name: string;
  state: string;
  enumeratedThrough: number;
  enumerationComplete: boolean;
  releasedThrough: number;
  succeededCount: number;
  failedCount: number;
  inFlight: number;
  remaining: number;
  tps: number;
  dailyQuota?: number | null;
  releasedToday: number;
  maxInFlight: number;
  windowStart?: string | null;
  windowEnd?: string | null;
  timeZoneId: string;
  windowOpenNow: boolean;
  etaSecondsAtCurrentTps?: number | null;
  createdAt: string;
  createdBy: string;
  completedAt?: string | null;
  lastVerb?: string | null;
  tenant?: string | null;
}

/** One failed item — the drill-down; REPLAY belongs to the jobs console. */
export interface CampaignFailedItem {
  seq: number;
  error?: string | null;
  completedAt?: string | null;
}

/** One audited verb against a campaign (who did what, newest first). */
export interface CampaignAuditEntry {
  id: number;
  at: string;
  actor: string;
  action: string;
  campaignId: string;
  detail?: string | null;
}

/**
 * The LIVE policy patch: every field is optional and a null one KEEPS its current value,
 * so the console sends only what the operator actually changed. Clearing is explicit —
 * `clearDailyQuota` / `clearWindow` exist because "no quota" cannot be said with a null.
 */
export interface CampaignThrottle {
  tps?: number;
  dailyQuota?: number;
  maxInFlight?: number;
  windowStart?: string;
  windowEnd?: string;
  timeZoneId?: string;
  clearDailyQuota?: boolean;
  clearWindow?: boolean;
}

/** One template with its live queue numbers and its retention promise. */
export interface NotificationTemplateStatus {
  key: string;
  hash: string;
  /** ISO-8601 duration or null — how long the rendered body survives (D4 retention). */
  deleteBodyAfter?: string | null;
  byState: Record<string, number>;
  oldestRequestedSeconds?: number | null;
}

/**
 * One notification as the ADMIN surface returns it. The recipient arrives MASKED from the
 * server (first character + domain hint); the console never has the full address and must
 * never present this field as if it were one.
 */
export interface NotificationInfo {
  id: string;
  dedupKey: string;
  template: string;
  templateHash: string;
  channel: string;
  maskedRecipient: string;
  culture: string;
  state: string;
  attempts: number;
  detail?: string | null;
  requestedAt: string;
  notBefore?: string | null;
  claimedAt?: string | null;
  sentAt?: string | null;
  failedAt?: string | null;
  bodyDeletedAt?: string | null;
  tenant?: string | null;
  correlationId?: string | null;
}

export interface AdminResult {
  ok: boolean;
  message: string;
}

/** Thrown for transport/status failures the caller must surface, never swallow. */
export class AdminHttpError extends Error {
  constructor(readonly status: number, readonly route: string) {
    super(`${route} answered ${status}`);
    this.name = "AdminHttpError";
  }
}

export class AdminClient {
  private readonly baseUrl: string;
  private readonly fetcher: typeof fetch;

  constructor(options: AdminClientOptions = {}) {
    this.baseUrl = (options.baseUrl ?? "").replace(/\/$/, "");
    // BIND the default: a bare `fetch` stored on an object and called as `this.fetcher(...)`
    // loses its receiver and throws "Illegal invocation" in a real browser — which the
    // discovery catch would have silently turned into "no capabilities" (found by the U2
    // Playwright gate; jsdom does not reproduce it, so the unit tests were green).
    this.fetcher = options.fetcher ?? ((input, init) => globalThis.fetch(input, init));
  }

  private async get<T>(route: string): Promise<T> {
    const response = await this.fetcher(`${this.baseUrl}${route}`, {
      headers: { accept: "application/json" },
      credentials: "include",
    });
    if (!response.ok) throw new AdminHttpError(response.status, route);
    return (await response.json()) as T;
  }

  /**
   * Capability discovery: probe each module's list root ONCE. 404 = absent (the module
   * was never composed). 401/403 = present but this operator may not see it — surfaced
   * as `forbidden` so the console can say WHY instead of hiding the panel silently.
   */
  async discoverCapabilities(): Promise<Record<ModuleName, "present" | "absent" | "forbidden">> {
    const entries = await Promise.all(
      MODULES.map(async (module) => {
        try {
          const response = await this.fetcher(`${this.baseUrl}${PROBE[module]}`, {
            headers: { accept: "application/json" },
            credentials: "include",
          });
          if (response.status === 404) return [module, "absent"] as const;
          if (response.status === 401 || response.status === 403) return [module, "forbidden"] as const;
          return [module, response.ok ? "present" : "absent"] as const;
        } catch {
          return [module, "absent"] as const;   // unreachable service: no panel, no crash
        }
      }),
    );
    return Object.fromEntries(entries) as Record<ModuleName, "present" | "absent" | "forbidden">;
  }

  fleets(): Promise<FleetInfo[]> {
    return this.get<FleetInfo[]>("/goldpath/admin/jobs/fleets");
  }

  jobs(fleet: string): Promise<JobInfo[]> {
    return this.get<JobInfo[]>(`/goldpath/admin/jobs/fleets/${encodeURIComponent(fleet)}/jobs`);
  }

  runs(fleet: string, options: { job?: string; take?: number } = {}): Promise<RunSummary[]> {
    const query = new URLSearchParams();
    if (options.job) query.set("job", options.job);
    // The contract clamps take to [1,500]; the console never asks for more.
    query.set("take", String(Math.min(500, Math.max(1, options.take ?? 50))));
    return this.get<RunSummary[]>(
      `/goldpath/admin/jobs/fleets/${encodeURIComponent(fleet)}/runs?${query.toString()}`,
    );
  }

  run(runId: string): Promise<RunDetail> {
    return this.get<RunDetail>(`/goldpath/admin/jobs/runs/${encodeURIComponent(runId)}`);
  }

  /** Every mutating verb answers the frozen envelope — 200 ok, 400 refusal, both typed. */
  async verb(route: string, body?: unknown): Promise<AdminResult> {
    const response = await this.fetcher(`${this.baseUrl}${route}`, {
      method: "POST",
      credentials: "include",
      headers: body === undefined ? { accept: "application/json" } : { accept: "application/json", "content-type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (response.status === 200 || response.status === 400) {
      return (await response.json()) as AdminResult;
    }

    throw new AdminHttpError(response.status, route);
  }

  triggerJob(fleet: string, job: string): Promise<AdminResult> {
    return this.verb(`/goldpath/admin/jobs/fleets/${encodeURIComponent(fleet)}/jobs/${encodeURIComponent(job)}/trigger`);
  }

  pauseJob(fleet: string, job: string): Promise<AdminResult> {
    return this.verb(`/goldpath/admin/jobs/fleets/${encodeURIComponent(fleet)}/jobs/${encodeURIComponent(job)}/pause`);
  }

  resumeJob(fleet: string, job: string): Promise<AdminResult> {
    return this.verb(`/goldpath/admin/jobs/fleets/${encodeURIComponent(fleet)}/jobs/${encodeURIComponent(job)}/resume`);
  }

  rerun(runId: string): Promise<AdminResult> {
    return this.verb(`/goldpath/admin/jobs/runs/${encodeURIComponent(runId)}/rerun`);
  }

  /**
   * Redrives EVERY open repair item of the run — the frozen verb takes no body and
   * scopes itself server-side (`RedrivenAt == null`). The console must not pretend to
   * select items it cannot select (review R1 on the U2 slice-1 PR).
   */
  replayItems(runId: string): Promise<AdminResult> {
    return this.verb(`/goldpath/admin/jobs/runs/${encodeURIComponent(runId)}/replay-items`);
  }

  bulkDefinitions(): Promise<BulkDefinitionStatus[]> {
    return this.get<BulkDefinitionStatus[]>("/goldpath/admin/bulk/definitions");
  }

  /**
   * The batch list. The frozen surface filters by STATE (and tenant) only — there is no
   * `definition` parameter, and the panel does NOT fake one client-side: narrowing the
   * take-bounded page the server returned would read as "no batches" while plenty exist.
   * The per-definition counts come from `/definitions`; issue #72 tracks adding the
   * server-side filter.
   */
  bulkBatches(options: { state?: string; take?: number } = {}): Promise<BulkBatchInfo[]> {
    const query = new URLSearchParams();
    if (options.state) query.set("state", options.state);
    query.set("take", String(Math.min(500, Math.max(1, options.take ?? 50))));
    return this.get<BulkBatchInfo[]>(`/goldpath/admin/bulk/batches?${query.toString()}`);
  }

  /** The VALIDATION report — keyset-paged by row number (the contract's own cursor). */
  bulkErrors(batchId: string, options: { afterRow?: number; take?: number } = {}): Promise<BulkRowError[]> {
    const query = new URLSearchParams();
    query.set("afterRow", String(options.afterRow ?? 0));
    query.set("take", String(Math.min(500, Math.max(1, options.take ?? 100))));
    return this.get<BulkRowError[]>(
      `/goldpath/admin/bulk/batches/${encodeURIComponent(batchId)}/errors?${query.toString()}`,
    );
  }

  approveBatch(batchId: string, note?: string): Promise<AdminResult> {
    return this.verb(`/goldpath/admin/bulk/batches/${encodeURIComponent(batchId)}/approve`, { note: note ?? null });
  }

  rejectBatch(batchId: string, note: string): Promise<AdminResult> {
    // The contract makes the note MANDATORY on reject — the refusal must teach later readers.
    return this.verb(`/goldpath/admin/bulk/batches/${encodeURIComponent(batchId)}/reject`, { note });
  }

  /**
   * Intake upload. The contract takes a RAW body on purpose (`curl --data-binary @file.csv`)
   * — no multipart, no antiforgery coupling — so the console posts the file bytes as-is.
   */
  async uploadBatch(definition: string, file: File, tenant?: string): Promise<BulkBatchInfo> {
    const query = new URLSearchParams({ fileName: file.name });
    if (tenant) query.set("tenant", tenant);
    const route = `/goldpath/admin/bulk/batches/${encodeURIComponent(definition)}?${query.toString()}`;
    const response = await this.fetcher(`${this.baseUrl}${route}`, {
      method: "POST",
      credentials: "include",
      headers: { accept: "application/json", "content-type": "application/octet-stream" },
      body: file,
    });
    if (!response.ok) throw new AdminHttpError(response.status, route);
    return (await response.json()) as BulkBatchInfo;
  }

  batch(batchId: string): Promise<BulkBatchInfo> {
    return this.get<BulkBatchInfo>(`/goldpath/admin/bulk/batches/${encodeURIComponent(batchId)}`);
  }

  campaigns(options: { state?: string; take?: number } = {}): Promise<CampaignInfo[]> {
    const query = new URLSearchParams();
    if (options.state) query.set("state", options.state);
    query.set("take", String(Math.min(500, Math.max(1, options.take ?? 50))));
    return this.get<CampaignInfo[]>(`/goldpath/admin/campaign/?${query.toString()}`);
  }

  campaign(id: string): Promise<CampaignInfo> {
    return this.get<CampaignInfo>(`/goldpath/admin/campaign/${encodeURIComponent(id)}`);
  }

  /** Execution failures — one noun across modules (bulk's `/errors` is validation). */
  campaignFailures(id: string, take = 100): Promise<CampaignFailedItem[]> {
    return this.get<CampaignFailedItem[]>(
      `/goldpath/admin/campaign/${encodeURIComponent(id)}/failures?take=${Math.min(500, Math.max(1, take))}`,
    );
  }

  campaignAudit(id: string, take = 100): Promise<CampaignAuditEntry[]> {
    return this.get<CampaignAuditEntry[]>(
      `/goldpath/admin/campaign/${encodeURIComponent(id)}/audit?take=${Math.min(500, Math.max(1, take))}`,
    );
  }

  pauseCampaign(id: string): Promise<AdminResult> {
    return this.verb(`/goldpath/admin/campaign/${encodeURIComponent(id)}/pause`, {});
  }

  resumeCampaign(id: string): Promise<AdminResult> {
    return this.verb(`/goldpath/admin/campaign/${encodeURIComponent(id)}/resume`, {});
  }

  abortCampaign(id: string, reason: string): Promise<AdminResult> {
    // The reason is the evidence — the contract binds the body, so it is never optional.
    return this.verb(`/goldpath/admin/campaign/${encodeURIComponent(id)}/abort`, { reason });
  }

  throttleCampaign(id: string, patch: CampaignThrottle): Promise<AdminResult> {
    return this.verb(`/goldpath/admin/campaign/${encodeURIComponent(id)}/throttle`, patch);
  }

  notificationTemplates(): Promise<NotificationTemplateStatus[]> {
    return this.get<NotificationTemplateStatus[]>("/goldpath/admin/notification/templates");
  }

  notifications(options: { state?: string; template?: string; take?: number } = {}): Promise<NotificationInfo[]> {
    const query = new URLSearchParams();
    if (options.state) query.set("state", options.state);
    if (options.template) query.set("template", options.template);
    query.set("take", String(Math.min(500, Math.max(1, options.take ?? 50))));
    return this.get<NotificationInfo[]>(`/goldpath/admin/notification/notifications?${query.toString()}`);
  }

  notification(id: string): Promise<NotificationInfo> {
    return this.get<NotificationInfo>(`/goldpath/admin/notification/notifications/${encodeURIComponent(id)}`);
  }

  /** The two focused lists the contract exposes in their own right. */
  notificationSuppressions(take = 100): Promise<NotificationInfo[]> {
    return this.get<NotificationInfo[]>(`/goldpath/admin/notification/suppressions?take=${Math.min(500, Math.max(1, take))}`);
  }

  notificationFailures(take = 100): Promise<NotificationInfo[]> {
    return this.get<NotificationInfo[]>(`/goldpath/admin/notification/failures?take=${Math.min(500, Math.max(1, take))}`);
  }
}

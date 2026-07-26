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

export interface RunDetail extends RunSummary {
  chunks: { index: number; status: string; attempts: number }[];
  failures: { itemKey: string; reason: string; chunkIndex: number }[];
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
    this.fetcher = options.fetcher ?? fetch;
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

  replayItems(runId: string, itemKeys: string[]): Promise<AdminResult> {
    return this.verb(`/goldpath/admin/jobs/runs/${encodeURIComponent(runId)}/replay-items`, { itemKeys });
  }
}

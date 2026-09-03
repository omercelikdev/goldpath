import { deadlineVerdict, humanizeSeconds } from "@qorpe/ui";
import { MODULES, type AdminClient, type ModuleName } from "./adminClient";
import { isUnreachable, type Capabilities } from "./sections";

/**
 * One thing worth an operator's attention. `tone` is the semantic ramp, not a priority
 * number: "danger" is something that already went wrong, "warning" is something that will
 * unless someone acts.
 */
export interface TriageRow {
  service: string;
  section: ModuleName;
  tone: "danger" | "warning";
  /**
   * A BLIND row is not another problem in the list — it says the list itself is
   * incomplete, which qualifies everything else on the screen. That is why it sorts ahead
   * even of failures (review R1: the tone alone put it below them, contradicting the very
   * claim the screen makes).
   */
  blind?: true;
  headline: string;
  detail: string;
}

/**
 * What one service contributed to Today: the attention rows, and the per-module counts
 * the stat cards print. A module appears in `stats` ONLY when its surface was actually
 * read to the end — a number the console could not read must not print as a zero.
 */
export interface ServiceTriage {
  rows: TriageRow[];
  stats: Partial<Record<ModuleName, number>>;
}

/** How much of each surface the triage reads. The contract is take-bounded everywhere. */
export const TRIAGE_TAKE = 50;

/**
 * The caveat the screen must SAY, because the numbers below are honest only within it:
 * the admin contract has no aggregate endpoint, so triage reads the same take-bounded
 * lists an operator would, and counts what it was given.
 */
export const TRIAGE_SCOPE = `the most recent ${TRIAGE_TAKE} rows of each surface`;

/**
 * Collects what is wrong on ONE service. Every number comes from the frozen contract's
 * own lists — the console invents no aggregate the API does not expose, and a capability
 * that is absent, forbidden or refusing contributes nothing rather than a guess.
 *
 * A surface that FAILS to answer is itself a row: an operator whose triage silently drops
 * a service is worse off than one who sees nothing at all.
 */
export async function collectServiceTriage(
  service: string,
  client: AdminClient,
  capabilities: Capabilities,
  now: Date = new Date(),
): Promise<ServiceTriage> {
  const rows: TriageRow[] = [];
  const stats: ServiceTriage["stats"] = {};
  const present = (module: ModuleName) => capabilities[module].kind === "present";

  // A surface the console cannot READ is triage's own blind spot, and blindness during an
  // incident is exactly what an operator must be told about. Grouped per service: five
  // identical "forbidden" rows would bury the estate's real problems under our own.
  const unreadable = MODULES.filter((module) => {
    const kind = capabilities[module].kind;
    return kind === "forbidden" || kind === "refused" || kind === "unreachable";
  });

  if (unreadable.length > 0) {
    // The filter above already established the kind; the message is all that is left to read.
    const said = (capabilities[unreadable[0]] as { message?: string }).message;
    rows.push({
      service,
      section: unreadable[0],
      tone: "danger",
      blind: true,
      // "Did not answer AT ALL" must mean exactly that — EVERY probe threw. One dead
      // probe among four honest refusals is not an outage, and reporting it as one is the
      // same false confidence this row exists to prevent (review R1 on this PR). The
      // shell's own predicate is reused rather than re-derived.
      headline: isUnreachable(capabilities)
        ? `${service} did not answer at all`
        : `${unreadable.length} surface${unreadable.length === 1 ? "" : "s"} on ${service} cannot be read`,
      detail: said
        ? `the service said: “${said}”`
        : isUnreachable(capabilities)
          ? "it may be down, or blocking this console's origin — triage cannot speak for it"
          : "this operator may not see them, or the request cannot be scoped",
    });
  }

  const unreachable = (section: ModuleName, what: string) =>
    rows.push({
      service,
      section,
      tone: "danger",
      headline: `${what} could not be read`,
      detail: "the surface answered, then stopped — triage cannot speak for it",
    });

  if (present("jobs")) {
    try {
      const fleets = await client.fleets();
      let failedRuns = 0;
      for (const fleet of fleets) {
        const runs = await client.runs(fleet.schedulerName, { take: TRIAGE_TAKE });
        const failed = runs.filter((run) => run.status === "Failed");
        const overrun = runs.filter((run) => deadlineVerdict(run, now) === "overrun" && run.status !== "Failed");
        const predicted = runs.filter((run) => deadlineVerdict(run, now) === "overrun-predicted");
        const repair = runs.reduce((sum, run) => sum + run.itemFailures, 0);
        failedRuns += failed.length;

        if (failed.length > 0) {
          rows.push({
            service,
            section: "jobs",
            tone: "danger",
            headline: `${failed.length} failed run${failed.length === 1 ? "" : "s"} in ${fleet.schedulerName}`,
            detail: failed.map((run) => run.jobName).slice(0, 3).join(", "),
          });
        }

        if (overrun.length > 0) {
          rows.push({
            service,
            section: "jobs",
            tone: "danger",
            headline: `${overrun.length} run${overrun.length === 1 ? "" : "s"} past the deadline in ${fleet.schedulerName}`,
            detail: overrun.map((run) => run.jobName).slice(0, 3).join(", "),
          });
        }

        if (predicted.length > 0) {
          rows.push({
            service,
            section: "jobs",
            tone: "warning",
            headline: `${predicted.length} run${predicted.length === 1 ? "" : "s"} predicted to overrun in ${fleet.schedulerName}`,
            detail: predicted.map((run) => run.jobName).slice(0, 3).join(", "),
          });
        }

        if (repair > 0) {
          rows.push({
            service,
            section: "jobs",
            tone: "warning",
            headline: `${repair} item${repair === 1 ? "" : "s"} waiting in the repair queue of ${fleet.schedulerName}`,
            detail: "replay them from the run console once the cause is fixed",
          });
        }
      }
      stats.jobs = failedRuns;
    } catch {
      unreachable("jobs", "the run surface");
    }
  }

  if (present("bulk")) {
    try {
      let awaiting = 0;
      for (const definition of await client.bulkDefinitions()) {
        awaiting += definition.awaitingApproval;
        if (definition.awaitingApproval > 0) {
          const age = definition.oldestAwaitingApprovalSeconds;
          rows.push({
            service,
            section: "bulk",
            tone: "warning",
            headline: `${definition.awaitingApproval} batch${definition.awaitingApproval === 1 ? "" : "es"} awaiting approval in ${definition.name}`,
            detail: age ? `the oldest has waited ${humanizeSeconds(age)}` : "a four-eyes gate is holding them",
          });
        }
      }
      stats.bulk = awaiting;
    } catch {
      unreachable("bulk", "the intake surface");
    }
  }

  if (present("campaign")) {
    try {
      let failedItems = 0;
      for (const campaign of await client.campaigns({ take: TRIAGE_TAKE })) {
        failedItems += campaign.failedCount;
        if (campaign.failedCount > 0) {
          rows.push({
            service,
            section: "campaign",
            tone: "warning",
            headline: `${campaign.failedCount} failed item${campaign.failedCount === 1 ? "" : "s"} in ${campaign.name}`,
            detail: `${campaign.succeededCount} succeeded · ${campaign.remaining} remaining`,
          });
        }

        if (campaign.state === "Paused") {
          rows.push({
            service,
            section: "campaign",
            tone: "warning",
            headline: `${campaign.name} is paused`,
            detail: `${campaign.remaining} items are waiting for someone to resume it`,
          });
        }
      }
      stats.campaign = failedItems;
    } catch {
      unreachable("campaign", "the campaign surface");
    }
  }

  if (present("notification")) {
    try {
      let failedSends = 0;
      for (const template of await client.notificationTemplates()) {
        const failed = template.byState.Failed ?? 0;
        failedSends += failed;
        if (failed > 0) {
          rows.push({
            service,
            section: "notification",
            tone: "danger",
            headline: `${failed} notification${failed === 1 ? "" : "s"} failed on ${template.key}`,
            detail: "the transport refused them — the evidence rows carry its words",
          });
        }

        const waiting = template.oldestRequestedSeconds ?? 0;
        if (waiting > 900) {
          rows.push({
            service,
            section: "notification",
            tone: "warning",
            headline: `${template.key} has a request waiting ${humanizeSeconds(waiting)}`,
            detail: "the send job may not be running",
          });
        }
      }
      stats.notification = failedSends;
    } catch {
      unreachable("notification", "the notification surface");
    }
  }

  if (present("approvals")) {
    try {
      const pending = await client.approvals({ status: ["Pending"], take: 500 });
      for (const request of pending) {
        rows.push({
          service,
          section: "approvals",
          tone: "warning",
          headline: `${request.subject} waits at ${request.pendingRole} (${request.ladder})`,
          detail: `requested by ${request.requestedBy}`,
        });
      }
      stats.approvals = pending.length;
    } catch {
      unreachable("approvals", "the approvals surface");
    }
  }

  if (present("fileexchange")) {
    try {
      let quarantined = 0;
      for (const rail of await client.fileRails()) {
        quarantined += rail.quarantineDepth;
        if (rail.quarantineDepth > 0) {
          rows.push({
            service,
            section: "fileexchange",
            tone: "warning",
            headline: `${rail.quarantineDepth} row${rail.quarantineDepth === 1 ? "" : "s"} in quarantine on ${rail.name}`,
            detail: "each carries its reason — a rising count on one rail is a counterparty format drift",
          });
        }
      }
      stats.fileexchange = quarantined;
    } catch {
      unreachable("fileexchange", "the file-rail surface");
    }
  }

  if (present("archival")) {
    try {
      let due = 0;
      for (const archive of await client.archiveDefinitions()) {
        due += archive.dueBacklog;
        if (archive.dueBacklog > 0) {
          rows.push({
            service,
            section: "archival",
            tone: "warning",
            headline: `${archive.dueBacklog} aggregate${archive.dueBacklog === 1 ? "" : "s"} due to archive in ${archive.name}`,
            detail: "the archive run has not caught up with them yet",
          });
        }
      }
      stats.archival = due;
    } catch {
      unreachable("archival", "the archival surface");
    }
  }

  return { rows, stats };
}

/**
 * Blind spots first, then danger, then warning, then by service. A row that says "this
 * list is incomplete" belongs above every row IN the list — otherwise the screen quietly
 * invites an operator to trust a picture it knows is partial.
 */
export function orderTriage(rows: TriageRow[]): TriageRow[] {
  const rank = (row: TriageRow) => (row.blind ? 0 : row.tone === "danger" ? 1 : 2);
  return [...rows].sort((left, right) => {
    const difference = rank(left) - rank(right);
    return difference !== 0 ? difference : left.service.localeCompare(right.service);
  });
}

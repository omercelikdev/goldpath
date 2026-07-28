import { createElement, type ReactNode } from "react";
import { Banner } from "@goldpath/kit";
import { Archive, Bell, CalendarClock, FileUp, LayoutDashboard, Megaphone } from "lucide-react";
import { MODULES, type AdminClient, type Capability, type ModuleName } from "./adminClient";
import { RunConsole } from "./RunConsole";
import { BulkPanel } from "./BulkPanel";
import { CampaignPanel } from "./CampaignPanel";
import { NotificationPanel } from "./NotificationPanel";
import { ArchivalPanel } from "./ArchivalPanel";

/** What the rail calls each module — the operator's word, not the package's. */
export const SECTION_LABEL: Record<ModuleName, string> = {
  jobs: "Runs",
  archival: "Archival",
  bulk: "Bulk intake",
  notification: "Notifications",
  campaign: "Campaigns",
};

/**
 * The rail's grouping (ui-standard v1.1 §7.2) — by CONCERN, not by package, so a future
 * module lands in a group instead of growing a flat list.
 */
/**
 * The rail's grouping (v1.1 §7.2, amended by the owner): the five modules are ONE family
 * and share ONE group — a heading must own several items to earn its place (the reference
 * does the same: its whole core domain is a single MOCKING group). A future surface for a
 * DIFFERENT audience (an API portal, platform settings) becomes its own group; a new
 * operations module simply joins this one.
 */
export const SECTION_GROUP: Record<ModuleName, string> = {
  jobs: "Modules",
  bulk: "Modules",
  campaign: "Modules",
  notification: "Modules",
  archival: "Modules",
};

/** One lucide icon per section — sparse by design; the icon IS the item when collapsed. */
export const SECTION_ICON: Record<ModuleName | "today", ReactNode> = {
  today: createElement(LayoutDashboard),
  jobs: createElement(CalendarClock),
  bulk: createElement(FileUp),
  notification: createElement(Bell),
  campaign: createElement(Megaphone),
  archival: createElement(Archive),
};

/** The one-line purpose sentence every screen opens with (v1.1 §7.8). */
export const SECTION_PURPOSE: Record<ModuleName | "today", string> = {
  today: "What is wrong across the estate, before you open anything.",
  jobs: "The fleet's scheduler: what runs, when, on which node — and the levers to stop it.",
  bulk: "Files in, validated row by row, executed only after a second pair of eyes.",
  notification: "Evidence for every send: delivered, suppressed, or failed — in the transport's own words.",
  campaign: "Paced fan-out under your hand: throttle, quota, window, stop.",
  archival: "The legal memory: sealed chains, holds, and erasures that leave a receipt.",
};

export type Capabilities = Record<ModuleName, Capability>;

/** The modules this service actually composes, in the standard order. */
export function composedSections(capabilities: Capabilities | null): ModuleName[] {
  return capabilities ? MODULES.filter((module) => capabilities[module].kind !== "absent") : [];
}

/** True when NOTHING answered — the service is down or blocking us, not module-less. */
export function isUnreachable(capabilities: Capabilities | null): boolean {
  return capabilities !== null && MODULES.every((module) => capabilities[module].kind === "unreachable");
}

export interface ServicePanelsProps {
  client: AdminClient;
  capabilities: Capabilities | null;
  section: ModuleName;
  now?: Date;
}

/**
 * One service's panels, without a shell around them — the shell belongs to the console as
 * a whole, because the operator's first screen is the estate, not any single service.
 *
 * A module the app never composed is absent (no panel, no dead link). One that is present
 * but REFUSING — no ops role, or no tenant to scope the call to — says exactly that, in
 * the server's words.
 */
export function ServicePanels({ client, capabilities, section, now }: ServicePanelsProps) {
  if (capabilities === null) {
    return <p className="text-sm text-muted-foreground">Discovering capabilities…</p>;
  }

  if (isUnreachable(capabilities)) {
    return (
      <Banner tone="danger" live="alert">
        This service did not answer at all — it may be down, or its CORS policy may not allow this console's
        origin. That is different from an app that composes no Goldpath module, and the console will not
        pretend otherwise.
      </Banner>
    );
  }

  if (composedSections(capabilities).length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No Goldpath admin surface answered here — this app composes none of them.
      </p>
    );
  }

  const capability = capabilities[section];

  return (
    <>
      {capability.kind === "forbidden" && (
        <Banner tone="warning">
          {SECTION_LABEL[section]} exists on this service, but your account lacks the ops role for it.
          {capability.message ? ` The service said: “${capability.message}”` : ""}
        </Banner>
      )}

      {capability.kind === "unreachable" && (
        <Banner tone="danger" live="alert">
          {SECTION_LABEL[section]} did not answer — the service may be down, or blocking this console's origin.
        </Banner>
      )}

      {capability.kind === "refused" && (
        <Banner tone="warning">
          {SECTION_LABEL[section]} is composed here but refused this request.
          {capability.message ? ` The service said: “${capability.message}”` : ""}
        </Banner>
      )}

      {capability.kind === "present" && section === "jobs" && <RunConsole client={client} now={now} />}
      {capability.kind === "present" && section === "bulk" && <BulkPanel client={client} />}
      {capability.kind === "present" && section === "campaign" && <CampaignPanel client={client} />}
      {capability.kind === "present" && section === "notification" && <NotificationPanel client={client} />}
      {capability.kind === "present" && section === "archival" && <ArchivalPanel client={client} />}
    </>
  );
}

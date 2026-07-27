import { Banner } from "@goldpath/kit";
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

export type Capabilities = Record<ModuleName, Capability>;

/** The modules this service actually composes, in the standard order. */
export function composedSections(capabilities: Capabilities | null): ModuleName[] {
  return capabilities ? MODULES.filter((module) => capabilities[module].kind !== "absent") : [];
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

  if (composedSections(capabilities).length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No Goldpath admin surface answered here — this app composes none, or the service is unreachable.
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

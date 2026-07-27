import { useEffect, useMemo, useState } from "react";
import { AppShell, Banner } from "@goldpath/kit";
import type { ShellNavItem } from "@goldpath/kit";
import { AdminClient, MODULES, type Capability, type ModuleName } from "./adminClient";
import { RunConsole } from "./RunConsole";
import { BulkPanel } from "./BulkPanel";
import { CampaignPanel } from "./CampaignPanel";
import { NotificationPanel } from "./NotificationPanel";
import { ArchivalPanel } from "./ArchivalPanel";

export interface ConsoleProps {
  /** Service root; omit for same-origin (the console is served BY the app it drives). */
  baseUrl?: string;
  title?: string;
  fetcher?: typeof fetch;
  now?: Date;
  /** The registry's other services — omitted entirely when there is only one. */
  services?: string[];
  activeService?: string;
  onSelectService?: (name: string) => void;
}

type Capabilities = Record<ModuleName, Capability>;

const SECTION_LABEL: Record<ModuleName, string> = {
  jobs: "Runs",
  archival: "Archival",
  bulk: "Bulk intake",
  notification: "Notifications",
  campaign: "Campaigns",
};

/**
 * The console shell: capability discovery decides what EXISTS, the shell renders only
 * that (console RFC §2). A module the app never composed is an absent section — never a
 * dead link, never a manifest upload. A capability that is present but REFUSING — no ops
 * role, or no tenant to scope the call to — says exactly that, in the server's words:
 * "absent" is reserved for a module the app genuinely does not compose.
 */
export function Console({
  baseUrl,
  title = "Goldpath console",
  fetcher,
  now,
  services,
  activeService,
  onSelectService,
}: ConsoleProps) {
  const client = useMemo(() => new AdminClient({ baseUrl, fetcher }), [baseUrl, fetcher]);
  const [capabilities, setCapabilities] = useState<Capabilities | null>(null);
  const [section, setSection] = useState<ModuleName>("jobs");
  const [collapsed, setCollapsed] = useState(false);

  useEffect(() => {
    let live = true;
    void client.discoverCapabilities().then((found) => {
      if (!live) return;
      setCapabilities(found);
      const first = MODULES.find((module) => found[module].kind !== "absent");
      if (first) setSection(first);
    });
    return () => {
      live = false;
    };
  }, [client]);

  const nav: ShellNavItem[] = capabilities
    ? MODULES.filter((module) => capabilities[module].kind !== "absent").map((module) => ({
        id: module,
        label: SECTION_LABEL[module],
        onSelect: () => setSection(module),
      }))
    : [];

  return (
    <AppShell
      title={title}
      nav={nav}
      activeId={section}
      services={services?.map((name) => ({ name, onSelect: () => onSelectService?.(name) }))}
      activeService={activeService}
      collapsed={collapsed}
      onToggleCollapsed={() => setCollapsed(!collapsed)}
    >
      {capabilities === null && <p className="text-sm text-muted-foreground">Discovering capabilities…</p>}

      {capabilities !== null && nav.length === 0 && (
        <p className="text-sm text-muted-foreground">
          No Goldpath admin surface answered here — this app composes none, or the service is unreachable.
        </p>
      )}

      {capabilities?.[section].kind === "forbidden" && (
        <Banner tone="warning">
          {SECTION_LABEL[section]} exists on this service, but your account lacks the ops role for it.
          {capabilities[section].message ? ` The service said: “${capabilities[section].message}”` : ""}
        </Banner>
      )}

      {capabilities?.[section].kind === "refused" && (
        // Composed, reachable, and REFUSING — the operator needs the server's reason
        // (a multi-tenant app scopes admin calls to the ambient tenant), not a blank screen.
        <Banner tone="warning">
          {SECTION_LABEL[section]} is composed here but refused this request.
          {capabilities[section].message ? ` The service said: “${capabilities[section].message}”` : ""}
        </Banner>
      )}

      {capabilities?.[section].kind === "present" && section === "jobs" && (
        <RunConsole client={client} now={now} />
      )}

      {capabilities?.[section].kind === "present" && section === "bulk" && <BulkPanel client={client} />}

      {capabilities?.[section].kind === "present" && section === "campaign" && <CampaignPanel client={client} />}

      {capabilities?.[section].kind === "present" && section === "notification" && <NotificationPanel client={client} />}

      {capabilities?.[section].kind === "present" && section === "archival" && <ArchivalPanel client={client} />}
    </AppShell>
  );
}

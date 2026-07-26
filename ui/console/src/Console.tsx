import { useEffect, useMemo, useState } from "react";
import { AppShell, Banner } from "@goldpath/kit";
import type { ShellNavItem } from "@goldpath/kit";
import { AdminClient, MODULES, type ModuleName } from "./adminClient";
import { RunConsole } from "./RunConsole";

export interface ConsoleProps {
  /** Service root; omit for same-origin (the console is served BY the app it drives). */
  baseUrl?: string;
  title?: string;
  fetcher?: typeof fetch;
  now?: Date;
}

type Capabilities = Record<ModuleName, "present" | "absent" | "forbidden">;

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
 * dead link, never a manifest upload. Panels beyond the run console land in U3; until
 * then a present-but-unbuilt capability says so honestly instead of pretending.
 */
export function Console({ baseUrl, title = "Goldpath console", fetcher, now }: ConsoleProps) {
  const client = useMemo(() => new AdminClient({ baseUrl, fetcher }), [baseUrl, fetcher]);
  const [capabilities, setCapabilities] = useState<Capabilities | null>(null);
  const [section, setSection] = useState<ModuleName>("jobs");
  const [collapsed, setCollapsed] = useState(false);

  useEffect(() => {
    let live = true;
    void client.discoverCapabilities().then((found) => {
      if (!live) return;
      setCapabilities(found);
      const first = MODULES.find((module) => found[module] !== "absent");
      if (first) setSection(first);
    });
    return () => {
      live = false;
    };
  }, [client]);

  const nav: ShellNavItem[] = capabilities
    ? MODULES.filter((module) => capabilities[module] !== "absent").map((module) => ({
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
      collapsed={collapsed}
      onToggleCollapsed={() => setCollapsed(!collapsed)}
    >
      {capabilities === null && <p className="text-sm text-muted-foreground">Discovering capabilities…</p>}

      {capabilities !== null && nav.length === 0 && (
        <p className="text-sm text-muted-foreground">
          No Goldpath admin surface answered here — this app composes none, or the service is unreachable.
        </p>
      )}

      {capabilities?.[section] === "forbidden" && (
        <Banner tone="warning">
          {SECTION_LABEL[section]} exists on this service, but your account lacks the ops role for it.
        </Banner>
      )}

      {capabilities?.[section] === "present" && section === "jobs" && (
        <RunConsole client={client} now={now} />
      )}

      {capabilities?.[section] === "present" && section !== "jobs" && (
        <p className="text-sm text-muted-foreground">
          {SECTION_LABEL[section]} is composed into this app; its panel ships in U3. Until then drive it
          through the admin API — the console adds no capability the API does not already expose.
        </p>
      )}
    </AppShell>
  );
}

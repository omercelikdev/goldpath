import type { ReactNode } from "react";

export interface ShellNavItem {
  /** Stable id — also the capability key when the console lights panels by discovery. */
  id: string;
  label: string;
  /** Absent means the capability is present but has nothing to count. */
  badge?: number;
  onSelect: () => void;
}

export interface ShellService {
  name: string;
  onSelect: () => void;
}

export interface AppShellProps {
  /** Product/tenant word in the rail head — the console is one shell, many services. */
  title: string;
  nav: ShellNavItem[];
  activeId: string;
  children: ReactNode;
  /** Cross-service registry entries; a single-service console omits it entirely. */
  services?: ShellService[];
  activeService?: string;
  collapsed?: boolean;
  onToggleCollapsed?: () => void;
  /**
   * Rendered at the rail foot — theme toggle, sign-out, whatever the app owns. Receives
   * the collapsed state so the caller can shrink with the rail (the eyes-on pass caught
   * a full-width control spilling out of the 74px rail).
   */
  footer?: (collapsed: boolean) => ReactNode;
}

/**
 * The app shell of ui-standard-v1 §3, vendored from the Mockifyr/Praxis layout: the PAGE
 * never scrolls — the rail sits flush in the frame and only the content surface scrolls
 * (its own `.scroll-area`). Capability-driven: the console renders the nav it is GIVEN,
 * so a missing module is simply an absent item, never a dead link.
 */
export function AppShell({
  title,
  nav,
  activeId,
  children,
  services,
  activeService,
  collapsed = false,
  onToggleCollapsed,
  footer,
}: AppShellProps) {
  return (
    <div data-testid="app-shell" className="flex h-dvh overflow-hidden bg-app">
      <aside
        data-collapsed={collapsed}
        className={`shrink-0 bg-app transition-[width] duration-300 ${collapsed ? "w-[74px]" : "w-[252px]"}`}
      >
        <nav aria-label="console sections" className="flex h-full flex-col gap-1 p-3">
          <div className="flex items-center justify-between px-2 py-3">
            {!collapsed && <span className="truncate text-sm font-semibold">{title}</span>}
            {onToggleCollapsed && (
              <button
                aria-label={collapsed ? "expand navigation" : "collapse navigation"}
                aria-expanded={!collapsed}
                className="rounded-md border border-border px-2 py-0.5 text-xs hover:bg-accent"
                onClick={onToggleCollapsed}
              >
                {collapsed ? "»" : "«"}
              </button>
            )}
          </div>

          {services && services.length > 0 && !collapsed && (
            <div className="mb-2 px-2">
              <label className="text-[11px] text-faint" htmlFor="goldpath-service">service</label>
              <select
                id="goldpath-service"
                className="mt-1 w-full rounded-md border border-border bg-background px-2 py-1 text-sm"
                value={activeService ?? services[0].name}
                onChange={(event) => services.find((s) => s.name === event.target.value)?.onSelect()}
              >
                {services.map((service) => (
                  <option key={service.name} value={service.name}>{service.name}</option>
                ))}
              </select>
            </div>
          )}

          {nav.map((item) => {
            const active = item.id === activeId;
            return (
              <button
                key={item.id}
                aria-current={active ? "page" : undefined}
                title={collapsed ? item.label : undefined}
                className={`flex items-center justify-between rounded-md px-3 py-2 text-sm ${
                  active ? "bg-primary text-primary-foreground" : "hover:bg-accent"
                }`}
                onClick={item.onSelect}
              >
                <span className={collapsed ? "sr-only" : "truncate"}>{item.label}</span>
                {collapsed && <span aria-hidden="true">{item.label.slice(0, 1).toUpperCase()}</span>}
                {item.badge !== undefined && item.badge > 0 && !collapsed && (
                  <span className="ml-2 rounded-full bg-danger-bg px-1.5 text-xs text-danger">{item.badge}</span>
                )}
              </button>
            );
          })}

          {footer && <div className="mt-auto px-2 pb-1">{footer(collapsed)}</div>}
        </nav>
      </aside>

      <div className="min-w-0 flex-1 p-3 ps-0">
        {/* The ONE scrolling surface — the frame stays put while content moves (§3). */}
        <main
          data-testid="shell-surface"
          className="scroll-area h-full overflow-y-auto rounded-2xl border border-border bg-surface p-6"
          style={{ boxShadow: "var(--shadow-surface)" }}
        >
          {children}
        </main>
      </div>
    </div>
  );
}

import { useEffect, type ReactNode } from "react";
import { ChevronsLeft, ChevronsRight, Search } from "lucide-react";
import { Select } from "./Select";
import { Tooltip } from "./Tooltip";

export interface ShellNavItem {
  /** Stable id — also the capability key when the console lights panels by discovery. */
  id: string;
  label: string;
  /** The item's lucide icon — the rail shows it always, and it IS the item when collapsed. */
  icon?: ReactNode;
  /** Small-caps group heading this item sits under (ui-standard v1.1 §7.2). */
  group?: string;
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
  /** The quiet second line under the title (the reference's "Mock Platform" slot). */
  subtitle?: string;
  nav: ShellNavItem[];
  activeId: string;
  children: ReactNode;
  /** Cross-service registry entries; a single-service console omits it entirely. */
  services?: ShellService[];
  activeService?: string;
  collapsed?: boolean;
  onToggleCollapsed?: () => void;
  /** Renders the rail's search trigger (v1.1 §7.14) — usually the kit's `openCommand`. */
  onSearch?: () => void;
  /** Rendered at the rail foot — theme toggle, sign-out, whatever the app owns. */
  footer?: (collapsed: boolean) => ReactNode;
}

/** localStorage key for the persisted rail state — same contract as the reference. */
export const COLLAPSE_KEY = "goldpath.ui.collapsed";

/**
 * Reads the persisted rail state once, for callers that own `collapsed` — the state
 * survives a reload because an operator who narrowed the rail meant it (v1.1 §7.3).
 */
export function initialCollapsed(): boolean {
  try {
    return globalThis.localStorage?.getItem(COLLAPSE_KEY) === "1";
  } catch {
    return false;
  }
}

/**
 * The app shell of ui-standard v1.1: grouped nav with icons, a 2px left-border accent on
 * the active item, and a collapse that keeps the ICONS — a collapsed rail is an icon
 * rail, not an empty gutter. The PAGE never scrolls; only the content surface does.
 */
export function AppShell({
  title,
  subtitle,
  nav,
  activeId,
  children,
  services,
  activeService,
  collapsed = false,
  onToggleCollapsed,
  onSearch,
  footer,
}: AppShellProps) {
  // Persist on change, wherever the state itself lives.
  useEffect(() => {
    try {
      globalThis.localStorage?.setItem(COLLAPSE_KEY, collapsed ? "1" : "0");
    } catch {
      /* private mode: the rail simply forgets */
    }
  }, [collapsed]);

  // Groups render in first-appearance order; ungrouped items form a nameless first group.
  const groups: { name: string | undefined; items: ShellNavItem[] }[] = [];
  for (const item of nav) {
    const bucket = groups.find((group) => group.name === item.group);
    if (bucket) bucket.items.push(item);
    else groups.push({ name: item.group, items: [item] });
  }

  return (
    <div data-testid="app-shell" className="flex h-dvh overflow-hidden bg-app">
      <aside
        data-collapsed={collapsed}
        className={`shrink-0 bg-app transition-[width] duration-300 ${collapsed ? "w-[74px]" : "w-[252px]"}`}
      >
        {/* The rail scrolls INDEPENDENTLY: a console composed of many capability panels
            must never clip its own nav inside the frame's overflow-hidden. */}
        <nav
          data-testid="shell-rail"
          aria-label="console sections"
          className="scroll-area flex h-full flex-col overflow-y-auto px-3 pb-3"
        >
          <div className={`flex items-center py-4 ${collapsed ? "justify-center" : "justify-between px-1"}`}>
            {!collapsed && (
              <span className="min-w-0">
                <span className="block truncate text-sm font-semibold">{title}</span>
                {subtitle && <span className="block truncate text-xs text-faint">{subtitle}</span>}
              </span>
            )}
            {onToggleCollapsed && (
              <button
                aria-label={collapsed ? "expand navigation" : "collapse navigation"}
                aria-expanded={!collapsed}
                className="shrink-0 rounded-lg p-1.5 text-faint transition-colors hover:bg-muted hover:text-foreground"
                onClick={onToggleCollapsed}
              >
                {collapsed ? <ChevronsRight size={18} aria-hidden="true" /> : <ChevronsLeft size={16} aria-hidden="true" />}
              </button>
            )}
          </div>

          {onSearch && (
            // Reference-exact trigger: the expanded rail shows the full search field with
            // its ⌘K hint; the collapsed rail keeps only the icon, centered like nav items.
            <div className="pb-2 pt-1">
              {collapsed ? (
                <Tooltip label="Search" side="right">
                  <button
                    aria-label="Search"
                    onClick={onSearch}
                    className="mx-auto flex h-9 w-10 items-center justify-center rounded-lg transition-colors hover:bg-muted"
                  >
                    <Search size={18} aria-hidden="true" className="text-muted-foreground" />
                  </button>
                </Tooltip>
              ) : (
                <button
                  onClick={onSearch}
                  className="flex h-9 w-full items-center gap-2.5 rounded-lg border border-border bg-muted/60 px-3 text-sm text-muted-foreground transition-colors hover:border-border-strong"
                >
                  <Search className="size-4" aria-hidden="true" />
                  <span>Search</span>
                  <kbd className="ms-auto rounded-md border border-border bg-background px-1.5 font-mono text-[11px]">⌘K</kbd>
                </button>
              )}
            </div>
          )}

          {services && services.length > 0 && !collapsed && (
            <div className="mb-2 px-1">
              <label className="control-label" htmlFor="goldpath-service">service</label>
              <Select
                id="goldpath-service"
                aria-label="service"
                className="mt-1 w-full"
                value={activeService ?? services[0].name}
                onChange={(name) => services.find((s) => s.name === name)?.onSelect()}
                options={services.map((service) => ({ value: service.name }))}
              />
            </div>
          )}

          {groups.map((group) => (
            <div key={group.name ?? "·"} className="mb-1">
              {group.name && !collapsed && (
                <div className="px-2.5 pb-1 pt-2.5 text-[10.5px] font-semibold uppercase tracking-wider text-faint">{group.name}</div>
              )}
              {/* Collapsed rails keep the grouping legible as thin separators. */}
              {group.name && collapsed && <div className="mx-3 my-2 h-px bg-border" aria-hidden="true" />}
              {group.items.map((item) => {
                const active = item.id === activeId;
                const button = (
                  <button
                    key={item.id}
                    aria-current={active ? "page" : undefined}
                    // Reference-exact (owner: "birebir Mockifyr"): items carry NO border;
                    // the active one gets the fill plus a short accent bar INSET at its
                    // left edge — a highlight, not a border.
                    className={`relative mb-0.5 flex h-9 items-center rounded-lg text-sm font-medium transition-colors ${
                      collapsed ? "mx-auto w-10 justify-center" : "w-full gap-2.5 px-2.5"
                    } ${
                      active
                        ? "bg-sidebar-accent font-semibold text-sidebar-accent-foreground"
                        : "text-muted-foreground hover:bg-muted hover:text-foreground"
                    }`}
                    onClick={item.onSelect}
                  >
                    {active && !collapsed && <span aria-hidden="true" className="absolute inset-y-1.5 start-0 w-[3px] rounded-full bg-primary" />}
                    {item.icon && <span aria-hidden="true" className="flex shrink-0 items-center [&>svg]:h-[18px] [&>svg]:w-[18px]">{item.icon}</span>}
                    <span className={collapsed ? "sr-only" : "truncate"}>{item.label}</span>
                    {!item.icon && collapsed && <span aria-hidden="true">{item.label.slice(0, 1).toUpperCase()}</span>}
                    {item.badge !== undefined && item.badge > 0 && !collapsed && (
                      <span className="ms-auto rounded-full bg-danger-bg px-1.5 text-xs text-danger">{item.badge}</span>
                    )}
                  </button>
                );
                // Collapsed, the NAME rides a real tooltip to the right of the rail —
                // the browser's title delay left the icons mute in practice (§8.5).
                return collapsed ? <Tooltip key={item.id} label={item.label} side="right">{button}</Tooltip> : button;
              })}
            </div>
          ))}

          {footer && <div className="mt-auto px-1 pb-1 pt-3">{footer(collapsed)}</div>}
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

export interface PageHeaderProps {
  title: string;
  /** The one-line purpose sentence (v1.1 §7.8) — what this screen answers, in words. */
  purpose: string;
  /** Right-aligned actions: refresh, primary verbs. */
  actions?: ReactNode;
}

/** Every screen opens with this: what am I looking at, and why does it exist. */
export function PageHeader({ title, purpose, actions }: PageHeaderProps) {
  return (
    <header className="mb-5 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h1 className="text-xl font-semibold">{title}</h1>
        <p className="mt-0.5 text-sm text-muted-foreground">{purpose}</p>
      </div>
      {actions && <div className="flex items-center gap-2">{actions}</div>}
    </header>
  );
}

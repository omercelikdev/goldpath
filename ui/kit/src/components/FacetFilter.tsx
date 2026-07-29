import * as DropdownMenu from "@radix-ui/react-dropdown-menu";
import { Check, ListFilter } from "lucide-react";

export interface FacetOption {
  value: string;
  label?: string;
  count?: number;
}

export interface FacetFilterProps {
  label: string;
  options: FacetOption[];
  selected: Set<string>;
  onToggle: (value: string) => void;
  onClear: () => void;
}

/**
 * The family's multi-select facet (v1.1 §7.5, class-verbatim from the reference).
 * Selecting keeps the menu OPEN (preventDefault on select) so several values toggle in
 * one visit; the trigger carries the active count. Semantics stay the console's rule:
 * whatever is selected travels to the SERVER as a filter — never a client-side narrow.
 */
export function FacetFilter({ label, options, selected, onToggle, onClear }: FacetFilterProps) {
  const n = selected.size;
  // Non-modal: a filter menu must not hide the page it filters — the operator keeps the
  // table and the other controls reachable while toggling.
  return (
    <DropdownMenu.Root modal={false}>
      <DropdownMenu.Trigger asChild>
        <button
          className={`inline-flex h-9 items-center gap-1.5 rounded-lg border px-3 text-sm font-medium transition-colors hover:bg-muted ${
            n > 0 ? "border-primary/50 bg-background text-foreground" : "border-border bg-background text-muted-foreground"
          }`}
        >
          <ListFilter size={16} aria-hidden="true" className="shrink-0" />
          {label}
          {n > 0 && (
            <span className="rounded-md bg-primary px-1.5 py-0.5 text-[11px] font-semibold tabular-nums text-primary-foreground">{n}</span>
          )}
        </button>
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content
          align="start"
          className="z-50 min-w-52 rounded-xl border border-border bg-background p-1.5 shadow-2xl"
        >
          {options.map((option) => {
            const on = selected.has(option.value);
            return (
              <DropdownMenu.Item
                key={option.value}
                onSelect={(event) => {
                  event.preventDefault();   // stay open: facets toggle in batches
                  onToggle(option.value);
                }}
                className="flex cursor-pointer select-none items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm outline-none transition-colors data-[highlighted]:bg-muted"
              >
                <span
                  aria-hidden="true"
                  className={`flex size-4 shrink-0 items-center justify-center rounded border ${
                    on ? "border-primary bg-primary text-primary-foreground" : "border-border-strong"
                  }`}
                >
                  {on && <Check size={12} />}
                </span>
                <span className="flex-1 truncate">{option.label ?? option.value}</span>
                {option.count != null && <span className="tabular-nums text-xs text-faint">{option.count}</span>}
              </DropdownMenu.Item>
            );
          })}
          {n > 0 && (
            <>
              <DropdownMenu.Separator className="my-1.5 h-px bg-border" />
              <DropdownMenu.Item
                onSelect={(event) => {
                  event.preventDefault();
                  onClear();
                }}
                className="cursor-pointer rounded-lg px-2.5 py-2 text-center text-xs font-medium text-muted-foreground outline-none transition-colors data-[highlighted]:bg-muted"
              >
                clear
              </DropdownMenu.Item>
            </>
          )}
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  );
}

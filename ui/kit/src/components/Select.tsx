import { useEffect, useId, useRef, useState } from "react";
import { Check, ChevronDown } from "lucide-react";

export interface SelectOption {
  value: string;
  /** Shown to the operator; the value itself when omitted. */
  label?: string;
}

export interface SelectProps {
  value: string;
  onChange: (value: string) => void;
  options: SelectOption[];
  /** The accessible name — a select without one is a mystery menu. */
  "aria-label": string;
  id?: string;
  className?: string;
  placeholder?: string;
  disabled?: boolean;
}

/**
 * The family select (v1.2 §8.7 as modernized in U9): the FAMILY's own listbox — the
 * platform popup retires. Hand-rolled combobox pattern rather than a library popover:
 * it must live INSIDE modal dialogs, and no portal machinery survives that in every
 * environment the suite runs in. Keyboard: arrows walk, Enter/Space choose, Escape
 * closes, Home/End jump; an outside click closes without choosing.
 */
export function Select({ value, onChange, options, id, className = "", placeholder, disabled, "aria-label": label }: SelectProps) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(() => Math.max(0, options.findIndex((option) => option.value === value)));
  const root = useRef<HTMLDivElement>(null);
  const listId = useId();

  // An outside pointer closes without choosing — the operator changed their mind.
  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open]);

  const chosen = options.find((option) => option.value === value);

  const openAt = (index: number) => {
    setActive(Math.max(0, index));
    setOpen(true);
  };

  const choose = (index: number) => {
    const option = options[index];
    if (option) onChange(option.value);
    setOpen(false);
  };

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Escape") {
      setOpen(false);
      return;
    }
    if (!open && (event.key === "Enter" || event.key === " " || event.key === "ArrowDown" || event.key === "ArrowUp")) {
      event.preventDefault();
      openAt(options.findIndex((option) => option.value === value));
      return;
    }
    if (!open) return;
    event.preventDefault();
    if (event.key === "ArrowDown") setActive((current) => Math.min(options.length - 1, current + 1));
    else if (event.key === "ArrowUp") setActive((current) => Math.max(0, current - 1));
    else if (event.key === "Home") setActive(0);
    else if (event.key === "End") setActive(options.length - 1);
    else if (event.key === "Enter" || event.key === " ") choose(active);
    else if (event.key === "Tab") setOpen(false);
  };

  return (
    <div ref={root} className={`relative inline-flex ${className}`}>
      <button
        type="button"
        id={id}
        role="combobox"
        aria-label={label}
        aria-expanded={open}
        aria-controls={listId}
        aria-haspopup="listbox"
        disabled={disabled}
        className="inline-flex h-9 w-full items-center justify-between gap-2 rounded-lg border border-input bg-background ps-3 pe-2.5 text-sm text-foreground outline-none transition-colors focus:border-border-strong disabled:opacity-50"
        onClick={() => (open ? setOpen(false) : openAt(options.findIndex((option) => option.value === value)))}
        onKeyDown={onKeyDown}
      >
        <span className={chosen ? "truncate" : "truncate text-muted-foreground"}>
          {chosen ? (chosen.label ?? chosen.value) : (placeholder ?? "choose…")}
        </span>
        <ChevronDown size={16} aria-hidden="true" className="shrink-0 text-muted-foreground" />
      </button>

      {open && (
        <ul
          id={listId}
          role="listbox"
          aria-label={label}
          className="absolute start-0 top-full z-50 mt-1 max-h-64 w-full min-w-max overflow-y-auto rounded-xl border border-border bg-background p-1.5 shadow-2xl"
        >
          {options.map((option, index) => (
            <li
              key={option.value}
              role="option"
              aria-selected={option.value === value}
              className={`flex cursor-pointer select-none items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm ${
                index === active ? "bg-muted" : ""
              }`}
              onPointerMove={() => setActive(index)}
              // Chosen on pointerDOWN so the choice lands before any outside-click logic.
              onPointerDown={(event) => {
                event.preventDefault();
                choose(index);
              }}
            >
              <span className="flex-1 truncate">{option.label ?? option.value}</span>
              {option.value === value && <Check size={14} aria-hidden="true" />}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

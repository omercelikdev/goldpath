import { Search, X } from "lucide-react";
import { useEffect, useState } from "react";

export interface SearchBoxProps {
  value: string;
  /** Fired on Enter and on blur — the server is asked once per COMMIT, not per keystroke. */
  onCommit: (value: string) => void;
  placeholder?: string;
  label: string;
}

/**
 * The family's search box (v1.1 §7.5, class-verbatim from the reference). It narrows
 * SERVER-side through the existing filters — never a loaded page: filtering client-side
 * would read as "no matches" while more sat behind the take bound.
 */
export function SearchBox({ value, onCommit, placeholder, label }: SearchBoxProps) {
  const [draft, setDraft] = useState(value);
  useEffect(() => setDraft(value), [value]);

  return (
    <label className="flex h-9 min-w-[220px] items-center gap-2 rounded-lg border border-border bg-muted/50 px-3 transition-colors focus-within:border-border-strong">
      <Search size={16} aria-hidden="true" className="shrink-0 text-muted-foreground" />
      <input
        aria-label={label}
        className="w-full bg-transparent text-sm outline-none placeholder:text-muted-foreground"
        value={draft}
        placeholder={placeholder}
        onChange={(event) => setDraft(event.target.value)}
        onBlur={() => draft !== value && onCommit(draft)}
        onKeyDown={(event) => {
          if (event.key === "Enter") onCommit(draft);
        }}
      />
      {/* Always laid out, only VISIBLE with text: appearing beside the input would
          shift every neighbour to the right (§8.10). */}
      <button
        aria-label="clear search"
        aria-hidden={draft.length === 0}
        tabIndex={draft.length > 0 ? 0 : -1}
        className={`shrink-0 text-muted-foreground transition-colors hover:text-foreground ${draft.length > 0 ? "visible" : "invisible"}`}
        onClick={() => {
          setDraft("");
          onCommit("");
        }}
      >
        <X size={14} aria-hidden="true" />
      </button>
    </label>
  );
}

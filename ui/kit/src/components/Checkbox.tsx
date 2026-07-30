import { Check } from "lucide-react";

export interface CheckboxProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  /** The visible label — a checkbox without one is a mystery box. */
  label: string;
  disabled?: boolean;
}

/**
 * The family checkbox (v1.2 §8.8): the FacetFilter's drawn box — primary fill, white
 * check — becomes the one checkbox everywhere. The native input stays in the tree
 * (sr-only) so forms, keyboards and assistive tech keep their real element.
 */
export function Checkbox({ checked, onChange, label, disabled }: CheckboxProps) {
  return (
    <label className={`inline-flex select-none items-center gap-2 text-sm ${disabled ? "opacity-50" : "cursor-pointer"}`}>
      <input
        type="checkbox"
        className="peer sr-only"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
      />
      <span
        aria-hidden="true"
        className={`flex size-4 shrink-0 items-center justify-center rounded border transition-colors peer-focus-visible:outline peer-focus-visible:outline-2 peer-focus-visible:outline-ring ${
          checked ? "border-primary bg-primary text-primary-foreground" : "border-border-strong bg-background"
        }`}
      >
        {checked && <Check size={12} />}
      </span>
      {label}
    </label>
  );
}

import { ChevronDown } from "lucide-react";
import type { ReactNode, SelectHTMLAttributes } from "react";

export interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  children: ReactNode;
}

/**
 * The family select (v1.2 §8.7): the NATIVE element — keyboard, screen reader and mobile
 * behaviour come free — dressed in the one field skin, with the family's chevron drawn
 * over the platform's. Options stay children, exactly like the element it wraps.
 */
export function Select({ children, className = "", ...rest }: SelectProps) {
  return (
    <span className={`relative inline-flex ${className}`}>
      <select
        {...rest}
        className="h-9 w-full appearance-none rounded-lg border border-input bg-background ps-3 pe-8 text-sm text-foreground outline-none transition-colors focus:border-border-strong"
      >
        {children}
      </select>
      <ChevronDown
        size={16}
        aria-hidden="true"
        className="pointer-events-none absolute end-2.5 top-1/2 -translate-y-1/2 text-muted-foreground"
      />
    </span>
  );
}

import * as TooltipPrimitive from "@radix-ui/react-tooltip";
import type { ReactNode } from "react";

export interface TooltipProps {
  /** What the tooltip SAYS — the name an icon-only control would otherwise hide. */
  label: string;
  /** Which side it opens on; collapsed-rail items read to the RIGHT of the rail. */
  side?: "top" | "right" | "bottom" | "left";
  children: ReactNode;
}

/**
 * The family tooltip (v1.2 §8.5, class-verbatim from the reference): an icon-only
 * control must SAY its name on hover and on focus — a real tooltip, not the browser's
 * title delay. Wraps exactly one focusable child.
 */
export function Tooltip({ label, side = "top", children }: TooltipProps) {
  return (
    <TooltipPrimitive.Provider delayDuration={150}>
      <TooltipPrimitive.Root>
        <TooltipPrimitive.Trigger asChild>{children}</TooltipPrimitive.Trigger>
        <TooltipPrimitive.Portal>
          <TooltipPrimitive.Content
            side={side}
            sideOffset={8}
            className="z-50 rounded-md bg-foreground px-2.5 py-1.5 text-xs font-medium text-background shadow-md"
          >
            {label}
          </TooltipPrimitive.Content>
        </TooltipPrimitive.Portal>
      </TooltipPrimitive.Root>
    </TooltipPrimitive.Provider>
  );
}

import * as DialogPrimitive from "@radix-ui/react-dialog";
import { X } from "lucide-react";
import type { ReactNode } from "react";

export interface DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  /** One line under the title — what this form DOES. */
  description?: string;
  children: ReactNode;
}

/**
 * The family's centred modal (v1.3 §9.5, class-verbatim from the reference's dialog):
 * add/edit forms open HERE, never at the bottom of the page. The Sheet stays the home
 * of ENTITY detail; the Dialog is the home of a decision or a form.
 */
export function Dialog({ open, onOpenChange, title, description, children }: DialogProps) {
  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/40" />
        <DialogPrimitive.Content className="fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border border-border bg-background p-6 shadow-2xl outline-none">
          <div className="flex items-start justify-between gap-3">
            <div>
              <DialogPrimitive.Title className="text-base font-semibold">{title}</DialogPrimitive.Title>
              {description ? (
                <DialogPrimitive.Description className="mt-1.5 text-sm text-muted-foreground">{description}</DialogPrimitive.Description>
              ) : (
                <DialogPrimitive.Description className="sr-only">{title}</DialogPrimitive.Description>
              )}
            </div>
            <DialogPrimitive.Close
              aria-label="close"
              className="shrink-0 rounded-lg p-1.5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
            >
              <X size={16} aria-hidden="true" />
            </DialogPrimitive.Close>
          </div>
          <div className="mt-4">{children}</div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}

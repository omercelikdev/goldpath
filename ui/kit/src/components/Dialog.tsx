import * as DialogPrimitive from "@radix-ui/react-dialog";
import type { ReactNode } from "react";
import { ModalClose, ModalDescription, ModalOverlay } from "./modal";

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
        <ModalOverlay />
        <DialogPrimitive.Content className="fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border border-border bg-background p-6 shadow-2xl outline-none">
          <div className="flex items-start justify-between gap-3">
            <div>
              <DialogPrimitive.Title className="text-base font-semibold">{title}</DialogPrimitive.Title>
              <ModalDescription description={description} title={title} className="mt-1.5 text-sm text-muted-foreground" />
            </div>
            <ModalClose className="shrink-0" />
          </div>
          <div className="mt-4">{children}</div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}

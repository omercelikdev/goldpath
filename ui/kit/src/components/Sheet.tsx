import * as Dialog from "@radix-ui/react-dialog";
import type { ReactNode } from "react";
import { ModalClose, ModalDescription, ModalOverlay } from "./modal";

export interface SheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The entity's name — the sheet's accessible title. */
  title: string;
  /** One line saying what the reader is looking at (v1.1 §7.4). */
  description?: string;
  children: ReactNode;
}

/**
 * The right-side detail panel of ui-standard v1.1 §7.4, reference-exact: a row click
 * opens the entity HERE instead of unfolding below the table. Radix Dialog carries the
 * a11y weight (focus trap, Escape, aria wiring) — the same primitive the reference uses.
 */
export function Sheet({ open, onOpenChange, title, description, children }: SheetProps) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <ModalOverlay />
        <Dialog.Content
          data-testid="sheet"
          className="fixed inset-y-0 end-0 z-50 flex w-full max-w-[680px] flex-col border-s border-border bg-background shadow-2xl outline-none"
        >
          <div className="border-b border-border px-6 py-4">
            <Dialog.Title className="text-base font-semibold">{title}</Dialog.Title>
            <ModalDescription description={description} title={title} className="mt-0.5 text-sm text-muted-foreground" />
          </div>
          <div className="scroll-area min-h-0 flex-1 overflow-y-auto px-6 py-4">{children}</div>
          <ModalClose className="absolute end-4 top-4" />
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

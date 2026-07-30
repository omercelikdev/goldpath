import * as DialogPrimitive from "@radix-ui/react-dialog";
import { X } from "lucide-react";

/**
 * The chrome Sheet and Dialog SHARE (review R6 on #125): one overlay, one named close,
 * one description-fallback — an a11y or style fix lands once. Internal to the kit; the
 * two public faces stay Sheet (entity detail) and Dialog (a form or a decision).
 */

export function ModalOverlay() {
  return <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/40" />;
}

export function ModalClose({ className }: { className: string }) {
  return (
    <DialogPrimitive.Close
      aria-label="close"
      className={`rounded-lg p-1.5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground ${className}`}
    >
      <X size={16} aria-hidden="true" />
    </DialogPrimitive.Close>
  );
}

/** Radix warns without a description; an entity with nothing to say still says so. */
export function ModalDescription({ description, title, className }: { description?: string; title: string; className: string }) {
  return description ? (
    <DialogPrimitive.Description className={className}>{description}</DialogPrimitive.Description>
  ) : (
    <DialogPrimitive.Description className="sr-only">{title}</DialogPrimitive.Description>
  );
}

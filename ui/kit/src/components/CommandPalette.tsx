import { useEffect, useState, type ReactNode } from "react";
import { Command } from "cmdk";
import { Search } from "lucide-react";

export interface CommandEntry {
  /** Stable id — cmdk also matches on it, so keep it wordlike. */
  id: string;
  label: string;
  icon?: ReactNode;
  run: () => void;
}

export interface CommandGroup {
  heading: string;
  items: CommandEntry[];
}

export interface CommandPaletteProps {
  /** Accessible dialog label; also the input's placeholder. */
  label?: string;
  groups: CommandGroup[];
}

/**
 * The event the sidebar search trigger dispatches — the palette listens for it, so the
 * trigger and the palette never need to know each other (the reference's contract).
 */
export const OPEN_COMMAND_EVENT = "goldpath:open-command";

/** Opens the palette from anywhere — the AppShell search trigger's default action. */
export function openCommand(): void {
  globalThis.dispatchEvent?.(new Event(OPEN_COMMAND_EVENT));
}

const GROUP_HEADING =
  "[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1.5 [&_[cmdk-group-heading]]:text-[10.5px] [&_[cmdk-group-heading]]:font-semibold [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:tracking-wider [&_[cmdk-group-heading]]:text-faint";

const ITEM =
  "flex cursor-pointer items-center gap-2.5 rounded-lg px-3 py-2 text-sm data-[selected=true]:bg-muted";

/**
 * The global command palette of ui-standard v1.1 §7.14: ⌘K/Ctrl-K toggles it, the
 * sidebar search trigger opens it, and every destination the nav offers is one keystroke
 * of fuzzy match away. Selection closes first, then runs — a command that navigates must
 * not race the dialog's own teardown.
 */
export function CommandPalette({ label = "Search", groups }: CommandPaletteProps) {
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setOpen((current) => !current);
      }
    };
    const onOpen = () => setOpen(true);
    document.addEventListener("keydown", onKey);
    globalThis.addEventListener(OPEN_COMMAND_EVENT, onOpen);
    return () => {
      document.removeEventListener("keydown", onKey);
      globalThis.removeEventListener(OPEN_COMMAND_EVENT, onOpen);
    };
  }, []);

  const run = (command: () => void) => {
    setOpen(false);
    command();
  };

  return (
    <Command.Dialog
      open={open}
      onOpenChange={setOpen}
      label={label}
      // The positioning lives on the CONTENT wrapper — the element that carries
      // role=dialog. Styling only a fixed child leaves the dialog element itself a
      // zero-height box, which reads as invisible to anything that measures (an a11y
      // tree, a Playwright toBeVisible). The Command in between contributes no box.
      contentClassName="fixed inset-0 z-[100] grid place-items-start justify-center pt-[16vh]"
      className="contents"
    >
      <div className="fixed inset-0 bg-black/40" onClick={() => setOpen(false)} />
      <div className="relative w-[640px] max-w-[calc(100vw-2rem)] overflow-hidden rounded-2xl border border-border bg-background shadow-[0_16px_50px_rgb(24_24_27/0.25)]">
        <div className="flex items-center gap-2.5 border-b border-border px-4">
          <Search className="size-4 text-muted-foreground" aria-hidden="true" />
          <Command.Input
            autoFocus
            placeholder={label}
            className="h-12 w-full bg-transparent text-sm outline-none placeholder:text-muted-foreground"
          />
        </div>
        <Command.List className="max-h-[340px] overflow-y-auto p-2">
          <Command.Empty className="px-3 py-6 text-center text-sm text-muted-foreground">—</Command.Empty>
          {groups.map((group) => (
            <Command.Group key={group.heading} heading={group.heading} className={GROUP_HEADING}>
              {group.items.map((item) => (
                <Command.Item key={item.id} value={`${item.label} ${item.id}`} onSelect={() => run(item.run)} className={ITEM}>
                  {item.icon && (
                    <span aria-hidden="true" className="flex shrink-0 items-center text-muted-foreground [&>svg]:size-4">
                      {item.icon}
                    </span>
                  )}
                  {item.label}
                </Command.Item>
              ))}
            </Command.Group>
          ))}
        </Command.List>
      </div>
    </Command.Dialog>
  );
}

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { Rows2, Rows3 } from "lucide-react";
import { Tooltip } from "./Tooltip";

export type Density = "comfortable" | "compact";

/** localStorage key for the persisted rhythm — same contract as the rail state. */
export const DENSITY_KEY = "goldpath.ui.density";

const DensityContext = createContext<{ density: Density; toggle: () => void }>({
  density: "comfortable",
  toggle: () => {},
});

function initialDensity(): Density {
  try {
    return globalThis.localStorage?.getItem(DENSITY_KEY) === "compact" ? "compact" : "comfortable";
  } catch {
    return "comfortable";
  }
}

/**
 * The journal's density feature (v1.3 §9.3): ONE rhythm for every family table, owned
 * here so a toggle on any screen changes all of them, and persisted like the rail —
 * an operator who tightened the rows meant it.
 */
export function DensityProvider({ children }: { children: ReactNode }) {
  const [density, setDensity] = useState<Density>(initialDensity);

  useEffect(() => {
    try {
      globalThis.localStorage?.setItem(DENSITY_KEY, density);
    } catch {
      /* private mode: the rhythm simply forgets */
    }
  }, [density]);

  return (
    <DensityContext.Provider value={{ density, toggle: () => setDensity(density === "compact" ? "comfortable" : "compact") }}>
      {children}
    </DensityContext.Provider>
  );
}

export function useDensity(): Density {
  return useContext(DensityContext).density;
}

/** Row padding per rhythm — read by every family table body cell. */
export function densityCell(density: Density): string {
  return density === "compact" ? "px-4 py-1.5" : "px-4 py-3";
}

/** The toolbar's density control: an icon action that says its state out loud. */
export function DensityToggle() {
  const { density, toggle } = useContext(DensityContext);
  const compact = density === "compact";
  return (
    <Tooltip label={compact ? "Comfortable rows" : "Compact rows"}>
      <button
        aria-label={compact ? "Comfortable rows" : "Compact rows"}
        aria-pressed={compact}
        className="inline-flex h-9 items-center justify-center rounded-lg border border-border bg-background px-2.5 transition-colors hover:bg-accent"
        onClick={toggle}
      >
        {compact ? <Rows3 size={16} aria-hidden="true" /> : <Rows2 size={16} aria-hidden="true" />}
      </button>
    </Tooltip>
  );
}

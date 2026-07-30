import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { Rows2, Rows3 } from "lucide-react";
import { IconAction } from "./IconAction";

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

/** The toolbar's density control: the IconAction, saying which rhythm a press GIVES. */
export function DensityToggle() {
  const { density, toggle } = useContext(DensityContext);
  const compact = density === "compact";
  return (
    <IconAction
      icon={compact ? <Rows3 /> : <Rows2 />}
      label={compact ? "Comfortable rows" : "Compact rows"}
      onClick={toggle}
      pressed={compact}
    />
  );
}

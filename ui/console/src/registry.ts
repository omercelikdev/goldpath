/**
 * The cross-service registry (console RFC §3). Within ONE app there is nothing to
 * configure — the fleets are store-discovered and the app's own admin surface speaks for
 * all of them. ACROSS services there is nothing to discover: a payments service cannot
 * know that a claims service exists, so the registry is CONFIG, and the console reads it
 * from where it is served rather than being rebuilt per adopter.
 */
export interface ServiceEntry {
  /** The operator's word for the service — what the rail shows. */
  name: string;
  /** Its management head; "" means the app serving this console (same-origin). */
  adminBaseUrl: string;
}

/** The shape of `console.config.json`, as an adopter writes it. */
interface RegistryFile {
  services?: { name?: unknown; adminBaseUrl?: unknown }[];
}

/** The single-service console: the app that serves the console is the only service. */
export const SAME_ORIGIN: ServiceEntry = { name: "this service", adminBaseUrl: "" };

/**
 * Reads the registry.
 *
 * Precedence, and why:
 * 1. `?base=` — the DEV override, and the escape hatch for pointing the console at a
 *    service it was not served by. One service, named by its own URL.
 * 2. `console.config.json` next to the console — the adopter's registry. Served by the
 *    app (or by whatever hosts the dist), so adding a service is a config change, never
 *    a rebuild.
 * 3. Nothing — same-origin, single service.
 *
 * A config that cannot be read or makes no sense yields the same-origin default AND says
 * so: a console that silently shows one service when the operator configured four would
 * hide exactly the outage they came to find.
 */
export interface Registry {
  services: ServiceEntry[];
  /** What went wrong, in words the console shows verbatim. */
  problem?: string;
  /** True when the problem cost us the registry entirely and we fell back to same-origin. */
  fellBack?: boolean;
}

export async function loadRegistry(
  fetcher: typeof fetch = (input, init) => globalThis.fetch(input, init),
  search: string = globalThis.location?.search ?? "",
): Promise<Registry> {
  const base = new URLSearchParams(search).get("base");
  if (base !== null) {
    return { services: [{ name: base || "same-origin", adminBaseUrl: base }] };
  }

  try {
    const response = await fetcher("console.config.json", { headers: { accept: "application/json" } });
    if (response.status === 404) return { services: [SAME_ORIGIN] };   // no registry: one service
    if (!response.ok) {
      return { services: [SAME_ORIGIN], problem: `the service registry answered ${response.status}`, fellBack: true };
    }

    const file = (await response.json()) as RegistryFile;
    const services = (file.services ?? [])
      .map((entry) => ({
        name: typeof entry.name === "string" ? entry.name.trim() : "",
        adminBaseUrl: typeof entry.adminBaseUrl === "string" ? entry.adminBaseUrl.trim() : "",
      }))
      .filter((entry) => entry.name.length > 0);

    if (services.length === 0) {
      return { services: [SAME_ORIGIN], problem: "the service registry lists no service with a name", fellBack: true };
    }

    // A PARTIAL drop is the dangerous one: the console still works, still looks right, and
    // is quietly missing a service the operator configured (review R1 on this PR).
    const dropped = (file.services ?? []).length - services.length;
    return dropped > 0
      ? { services, problem: `${dropped} registry entr${dropped === 1 ? "y has" : "ies have"} no name and ${dropped === 1 ? "was" : "were"} skipped` }
      : { services };
  } catch {
    return { services: [SAME_ORIGIN], problem: "the service registry could not be read", fellBack: true };
  }
}

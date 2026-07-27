import { useEffect, useState } from "react";
import { Banner } from "@goldpath/kit";
import { Console } from "./Console";
import { loadRegistry, SAME_ORIGIN, type ServiceEntry } from "./registry";

export interface ConsoleAppProps {
  fetcher?: typeof fetch;
  /** Injected in tests; defaults to the browser's own query string. */
  search?: string;
  now?: Date;
}

/**
 * The console, across services (console RFC §3 + D2). It owns exactly two things the
 * per-service screen must not: WHICH services exist (config, not discovery) and which one
 * the operator is looking at.
 *
 * Each service gets its own `Console` instance, keyed by name, so switching re-runs
 * capability discovery from scratch — a payments service's panels must never be shown
 * under a claims service's name because the shell happened to reuse the component.
 */
export function ConsoleApp({ fetcher, search, now }: ConsoleAppProps) {
  const [services, setServices] = useState<ServiceEntry[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [active, setActive] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    void loadRegistry(fetcher, search).then((result) => {
      if (!live) return;
      setServices(result.services);
      setProblem(result.problem ?? null);
      setActive((current) => current ?? result.services[0]?.name ?? null);
    });
    return () => {
      live = false;
    };
  }, [fetcher, search]);

  if (services === null) {
    return <p className="p-6 text-sm text-muted-foreground">Reading the service registry…</p>;
  }

  const service = services.find((entry) => entry.name === active) ?? services[0] ?? SAME_ORIGIN;

  return (
    <>
      {problem && (
        // Config that failed to load is NOT a quiet fallback: an operator who configured
        // four services and sees one is looking at the wrong console.
        <Banner tone="warning" live="alert">
          {problem} — showing this service only.
        </Banner>
      )}
      <Console
        key={service.name}
        baseUrl={service.adminBaseUrl}
        title={service.name}
        fetcher={fetcher}
        now={now}
        services={services.length > 1 ? services.map((entry) => entry.name) : undefined}
        activeService={service.name}
        onSelectService={setActive}
      />
    </>
  );
}

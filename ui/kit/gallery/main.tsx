// The kit gallery: every composite, both themes, real tokens — the screen the eye
// verifies BEFORE any slice ships (U1 exit gate; screenshots ride the PRs).
import { StrictMode, useState } from "react";
import { createRoot } from "react-dom/client";
import "../src/tokens/tokens.css";
import { StateBadge } from "../src/components/StateBadge";

const STATES = [
  "Completed", "Running", "Failed", "Resumed",
  "Received", "Validated", "Executing", "CompletedWithFailures", "Rejected",
  "Requested", "Sent", "Suppressed",
  "Submitted", "PendingApproval", "Executed",
  "SomethingUnknown",
];

function Gallery() {
  const [dark, setDark] = useState(false);
  return (
    <div className={dark ? "dark" : ""}>
      <div className="min-h-screen bg-app text-foreground p-8" style={{ minHeight: "100vh" }}>
        <div className="bg-surface rounded-2xl p-6">
          <div className="flex items-center justify-between mb-6">
            <h1 className="text-lg font-semibold">@goldpath/kit — gallery</h1>
            <button
              className="rounded-md border border-border bg-background px-3 py-1.5 text-sm hover:bg-accent"
              onClick={() => {
                document.documentElement.classList.toggle("dark", !dark);
                setDark(!dark);
              }}
            >
              {dark ? "light" : "dark"} theme
            </button>
          </div>
          <section className="bg-background rounded-lg border border-border p-5" style={{ boxShadow: "var(--shadow-surface)" }}>
            <h2 className="text-sm font-medium text-muted-foreground mb-4">StateBadge — the §5 ramp, every domain state</h2>
            <div className="flex flex-wrap gap-2">
              {STATES.map((s) => <StateBadge key={s} state={s} />)}
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}

createRoot(document.getElementById("root")!).render(<StrictMode><Gallery /></StrictMode>);

import { useState, type ReactNode } from "react";
import { Check, Copy } from "lucide-react";
import { IconAction } from "./IconAction";

/**
 * The journal's detail anatomy (v1.3 §9.4), for the inside of a Sheet: small-caps
 * section headings, key-value rows as one bordered card, and code blocks with a copy
 * button — never a plain run of text.
 */

export function DetailSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mt-5 first:mt-0">
      <h4 className="mb-2 text-[10.5px] font-semibold uppercase tracking-wider text-faint">{title}</h4>
      {children}
    </section>
  );
}

export interface KeyValueRow {
  key: string;
  value: ReactNode;
  /** Identities and hashes read mono; prose does not. */
  mono?: boolean;
}

export function KeyValueRows({ rows }: { rows: KeyValueRow[] }) {
  return (
    <dl className="divide-y divide-border rounded-xl border border-border bg-background">
      {rows.map((row) => (
        <div key={row.key} className="flex items-baseline gap-4 px-3.5 py-2.5 text-sm">
          <dt className="w-36 shrink-0 text-xs text-muted-foreground">{row.key}</dt>
          <dd className={`min-w-0 flex-1 break-all ${row.mono ? "font-mono text-xs" : ""}`}>{row.value}</dd>
        </div>
      ))}
    </dl>
  );
}

export function CodeBlock({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="relative rounded-xl border border-border bg-muted/40 p-3.5">
      <pre className="scroll-area overflow-x-auto pe-10 font-mono text-xs leading-relaxed">{text}</pre>
      <span className="absolute end-2 top-2">
        <IconAction
          icon={copied ? <Check /> : <Copy />}
          label={copied ? "Copied" : "Copy"}
          onClick={() => {
            void navigator.clipboard?.writeText(text).then(() => {
              setCopied(true);
              setTimeout(() => setCopied(false), 2000);
            });
          }}
        />
      </span>
    </div>
  );
}

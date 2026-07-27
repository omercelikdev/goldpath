/**
 * One duration vocabulary for the whole console (ui-standard-v1 §3: the same fact reads
 * the same everywhere). Every panel measures ages the operator must judge at a glance —
 * how long a batch has waited at the gate, how long a campaign still needs, how long the
 * oldest notification has sat in the queue — and three hand-rolled copies of the same
 * ladder had already drifted apart in their tiers.
 *
 * The ladder is deliberately coarse: an operator reads "2h", not "7 213 seconds".
 */
export function humanizeSeconds(seconds: number): string {
  const value = Math.max(0, seconds);
  if (value < 90) return `${Math.round(value)}s`;
  if (value < 5400) return `${Math.round(value / 60)}m`;          // up to 90 minutes
  if (value < 172_800) return `${Math.round(value / 3600)}h`;     // up to 48 hours
  return `${Math.round(value / 86_400)}d`;
}

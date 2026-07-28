/**
 * An ISO timestamp, shortened for a table cell: date and wall-clock seconds, no 'T', no
 * sub-second digits, no offset noise. STRING surgery on purpose — parsing through `Date`
 * would shift the value into the viewer's zone and silently disagree with the server's
 * logs, which is the one thing an incident timeline must never do. The full raw value
 * belongs in the element's `title`, so nothing is lost, only quieted.
 */
export function shortStamp(iso: string | null | undefined): string {
  if (!iso) {
    return "—";
  }

  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2}:\d{2})/.exec(iso);
  return match ? `${match[1]} ${match[2]}` : iso;
}

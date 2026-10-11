/** Normalises stored icon values ("fa-house", "fa-solid fa-house", "house") to a bare name. */
export function iconName(value: string | null | undefined): string {
  if (!value) return '';
  const parts = value.trim().split(/\s+/);
  const last = parts[parts.length - 1] ?? '';
  return last.startsWith('fa-') ? last.slice(3) : last;
}

/**
 * Icons whose name contains every search term; names starting with the first term come first.
 * Results are capped so the picker grid stays light.
 */
export function searchIcons(names: readonly string[], query: string, limit = 120): string[] {
  const terms = query.trim().toLowerCase().replace(/^fa-/, '').split(/[\s-]+/).filter(Boolean);
  if (terms.length === 0) return names.slice(0, limit);
  const matches = names.filter(name => terms.every(term => name.includes(term)));
  const [first] = terms;
  matches.sort((a, b) => Number(!a.startsWith(first)) - Number(!b.startsWith(first)) || a.length - b.length || a.localeCompare(b));
  return matches.slice(0, limit);
}

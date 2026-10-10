/** Locale-aware singular/plural labels for the integer rule counts shown in Profile and reviews. */
export function countLabel<Key extends string>(
  count: number,
  t: (key: Key) => string,
  singular: Key,
  plural: Key,
): string {
  return `${count} ${t(count === 1 ? singular : plural)}`;
}

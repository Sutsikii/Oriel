// TanStack Query and Oriel: C# events invalidate the queries of what they
// changed, so the page refetches. Any QueryClient (React, Vue, Solid,
// Svelte) works: only invalidateQueries is used.
import type { Subscribe } from "oriel-runtime";

/** What of a QueryClient this needs: TanStack Query's, of any framework. */
export interface Invalidates {
  invalidateQueries(filters: { queryKey: readonly unknown[] }): unknown;
}

/**
 * Invalidates the queries of `queryKey` each time the C# event of `subscribe`
 * fires: `invalidateOn(queryClient, Decks.onChanged, ["decks"])`. Returns a
 * function that stops.
 */
export function invalidateOn(client: Invalidates, subscribe: Subscribe<unknown>, queryKey: readonly unknown[]): () => void {
  return subscribe(() => void client.invalidateQueries({ queryKey }));
}

/**
 * As `invalidateOn`, with the key of each query made from the event's
 * payload: `invalidateOnPayload(queryClient, Decks.onChanged, (deck) => ["deck", deck.id])`.
 */
export function invalidateOnPayload<T>(client: Invalidates, subscribe: Subscribe<T>, queryKey: (payload: T) => readonly unknown[]): () => void {
  return subscribe((payload) => void client.invalidateQueries({ queryKey: queryKey(payload) }));
}

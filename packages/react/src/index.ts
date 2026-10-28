// React hooks for Oriel's generated clients: C# events and calls, with
// what React needs (unsubscribing, ignoring stale results, refreshing).
import { useCallback, useEffect, useRef, useState, type DependencyList } from "react";
import type { Subscribe } from "oriel-runtime";

/**
 * Listens to a C# event while the component is mounted:
 * `useOrielEvent(Clock.onTick, (now) => setNow(now))`. The listener may
 * change on each render; the event is listened to once.
 */
export function useOrielEvent<T>(subscribe: Subscribe<T>, listener: (payload: T) => void): void {
  const latest = useRef(listener);
  useEffect(() => {
    latest.current = listener;
  });
  useEffect(() => subscribe((payload) => latest.current(payload)), [subscribe]);
}

/** A call's result, as a component shows it. */
export interface OrielQuery<T> {
  /** The result of the last call that returned, or undefined before. */
  readonly data: T | undefined;
  /** What the last call threw, often a CallError, or undefined. */
  readonly error: unknown;
  /** Whether a call is under way. */
  readonly loading: boolean;
  /** Calls again. */
  refresh(): void;
}

/**
 * Calls C# and holds its result: `useOrielQuery(() => Decks.open(path), [path])`.
 * It calls again when `deps` change, when `refresh()` is called, and when one
 * of the events of `refreshOn` fires, such as `Decks.onChanged`. Results of
 * calls overtaken by a newer one are dropped.
 */
export function useOrielQuery<T>(
  query: () => Promise<T>,
  deps: DependencyList,
  options: { refreshOn?: readonly Subscribe<unknown>[] } = {},
): OrielQuery<T> {
  const [state, setState] = useState<{ data: T | undefined; error: unknown; loading: boolean }>({
    data: undefined,
    error: undefined,
    loading: true,
  });
  const [round, setRound] = useState(0);
  const latest = useRef(query);
  latest.current = query;
  const refresh = useCallback(() => setRound((n) => n + 1), []);

  useEffect(() => {
    let current = true;
    setState((s) => ({ ...s, loading: true }));
    latest.current().then(
      (data) => current && setState({ data, error: undefined, loading: false }),
      (error: unknown) => current && setState((s) => ({ data: s.data, error, loading: false })),
    );
    return () => {
      current = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, round]);

  const refreshOn = options.refreshOn ?? [];
  useEffect(() => {
    const stops = refreshOn.map((subscribe) => subscribe(refresh));
    return () => stops.forEach((stop) => stop());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, refreshOn);

  return { ...state, refresh };
}

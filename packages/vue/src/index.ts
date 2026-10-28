// Vue composables for Oriel's generated clients: C# events and calls, tied
// to the component's (or effect scope's) life.
import { getCurrentScope, onScopeDispose, ref, shallowRef, watch, type Ref, type WatchSource } from "vue";
import type { Subscribe } from "oriel-runtime";

/** Listens to a C# event until the component, or effect scope, goes: `useOrielEvent(Clock.onTick, (now) => (time.value = now))`. */
export function useOrielEvent<T>(subscribe: Subscribe<T>, listener: (payload: T) => void): () => void {
  const stop = subscribe(listener);
  if (getCurrentScope()) onScopeDispose(stop);
  return stop;
}

/** A call's result, as refs. */
export interface OrielQuery<T> {
  /** The result of the last call that returned, or undefined before. */
  readonly data: Ref<T | undefined>;
  /** What the last call threw, often a CallError, or undefined. */
  readonly error: Ref<unknown>;
  /** Whether a call is under way. */
  readonly loading: Ref<boolean>;
  /** Calls again. */
  refresh(): void;
}

/**
 * Calls C# and holds its result: `useOrielQuery(() => Decks.open(path.value), { watch: path })`.
 * It calls again when `watch` changes, when `refresh()` is called, and when
 * one of the events of `refreshOn` fires. Results of calls overtaken by a
 * newer one are dropped.
 */
export function useOrielQuery<T>(
  query: () => Promise<T>,
  options: { watch?: WatchSource | WatchSource[]; refreshOn?: readonly Subscribe<unknown>[] } = {},
): OrielQuery<T> {
  const data = shallowRef<T | undefined>(undefined);
  const error = shallowRef<unknown>(undefined);
  const loading = ref(true);
  let round = 0;

  function refresh(): void {
    const mine = ++round;
    loading.value = true;
    query().then(
      (result) => {
        if (mine !== round) return;
        data.value = result;
        error.value = undefined;
        loading.value = false;
      },
      (err: unknown) => {
        if (mine !== round) return;
        error.value = err;
        loading.value = false;
      },
    );
  }

  if (options.watch) watch(options.watch, refresh);
  for (const subscribe of options.refreshOn ?? []) useOrielEvent(subscribe, refresh);
  refresh();
  return { data, error, loading, refresh };
}

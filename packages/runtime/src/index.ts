// The runtime of Oriel's TypeScript clients: what a generated client
// carries, as a package. Clients written with OrielTypeScriptRuntime set to
// "oriel-runtime" import it from here; framework adapters build on it.

/** What `window.oriel`, the page runtime Oriel injects into its windows, offers. */
export interface Bridge {
  call(method: string, args: unknown[]): Promise<unknown>;
  on(event: string, listener: (payload: unknown) => void): () => void;
  stream(method: string, args: unknown[]): AsyncIterable<unknown>;
}

class CallErrorClass extends Error {
  /** The method that failed, as "Service.Method". */
  readonly method: string;
  /** The code of a [ServiceError] exception, or null. */
  readonly code: string | null;
  /** The public properties of a [ServiceError] exception, or null. */
  readonly data: unknown;

  constructor(method: string, message: string, code: string | null = null, data: unknown = null) {
    super(message);
    this.name = "CallError";
    this.method = method;
    this.code = code;
    this.data = code === null ? null : data;
  }
}

// One class for every client, generated or not, so that instanceof CallError holds whichever threw.
const callErrors = globalThis as { [key: symbol]: unknown };

/**
 * The class of the errors calls reject with. Generated clients export it as
 * `CallError`, typed by the codes of their app's errors.
 */
export const CallErrorBase = (callErrors[Symbol.for("oriel.CallError")] ??= CallErrorClass) as typeof CallErrorClass;
export type CallErrorBase = CallErrorClass;

/** The page runtime; throws outside an Oriel window. */
export function bridge(): Bridge {
  const runtime = (globalThis as { oriel?: Bridge }).oriel;
  if (!runtime) throw new Error("Oriel: window.oriel is missing; open this page in an Oriel window");
  return runtime;
}

function rethrow(method: string, err: unknown): never {
  if (err instanceof Error && err.name === "CallError") {
    const { code, data } = err as { code?: string | null; data?: unknown };
    throw new CallErrorBase(method, err.message, code ?? null, data);
  }
  throw err;
}

/** Calls `method` ("Service.Method") with `args`; resolves with its result. */
export async function call<T>(method: string, ...args: unknown[]): Promise<T> {
  // Arguments left out stay out, so that C# uses its defaults.
  while (args.length > 0 && args[args.length - 1] === undefined) args.pop();
  try {
    return (await bridge().call(method, args)) as T;
  } catch (err) {
    return rethrow(method, err);
  }
}

/** Calls `method`, which returns a stream; its items, for `for await`. */
export function stream<T>(method: string, ...args: unknown[]): AsyncIterable<T> {
  while (args.length > 0 && args[args.length - 1] === undefined) args.pop();
  return {
    [Symbol.asyncIterator](): AsyncIterator<T> {
      const items = bridge().stream(method, args)[Symbol.asyncIterator]();
      return {
        async next(): Promise<IteratorResult<T>> {
          try {
            return (await items.next()) as IteratorResult<T>;
          } catch (err) {
            return rethrow(method, err);
          }
        },
        async return(): Promise<IteratorResult<T>> {
          return (await items.return!()) as IteratorResult<T>;
        },
      };
    },
  };
}

/** Listens to the event `event` ("Service.Event"); returns a function that stops listening. */
export function on<T>(event: string, listener: (payload: T) => void): () => void {
  return bridge().on(event, listener as (payload: unknown) => void);
}

/** A function that listens, and returns a function that stops: what `Service.onEvent` is. */
export type Subscribe<T> = (listener: (payload: T) => void) => () => void;

/**
 * Fires when files are dropped on this window, with their full paths and where they were
 * dropped, in CSS pixels of the page. Returns a function that stops listening.
 */
export function onFilesDropped(listener: (drop: { paths: string[]; x: number; y: number }) => void): () => void {
  return on("oriel:filesDropped", listener);
}

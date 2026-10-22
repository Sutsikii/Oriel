# Services and the TypeScript client

Pages talk to C# through **services**: classes marked `[Service]` whose
public methods pages call and whose events pages listen to. The source
generator reads them at compile time and writes two things:

- a **dispatcher** per service, which reads the arguments of a call from
  JSON, calls the method and writes its result, without reflection;
- the **TypeScript client**, a module with the types the services use and a
  typed function per method and event.

## Services

```csharp
/// <summary>Opens and saves decks.</summary>
[Service]
public class Decks
{
    /// <summary>Reads the deck at <paramref name="path"/>.</summary>
    /// <param name="path">Where the deck is.</param>
    /// <returns>The deck, or null when there is none.</returns>
    public async Task<Deck?> Open(string path) { /* ... */ }

    public Task Save(string path, Deck deck) { /* ... */ }

    /// <summary>Fires when a deck changed on disk.</summary>
    public Event<Deck> Changed { get; } = new();
}
```

```ts
// Generated in oriel.ts
export const Decks = {
  /**
   * Reads the deck at path.
   * @param path Where the deck is.
   * @returns The deck, or null when there is none.
   */
  open(path: string): Promise<Deck | null> { ... },
  save(path: string, deck: Deck): Promise<void> { ... },
  /** Fires when a deck changed on disk. ... */
  onChanged(listener: (payload: Deck) => void): () => void { ... },
} as const;
```

What pages see:

| C# | Pages |
|---|---|
| A public instance method `Open` | `Decks.open(...)`, returning a `Promise` |
| `void`, `Task`, `ValueTask` | `Promise<void>` |
| `T`, `Task<T>`, `ValueTask<T>` | `Promise<T>` |
| A thrown exception | A rejected promise with a `CallError` (`err.message`, `err.method`) |
| An `Event<T>` property `Changed` | `Decks.onChanged(listener)`, returning a function that stops listening |
| `[Service(Name = "Files")]` | The service is called `Files` in pages |
| `[ServiceIgnore]` on a public method | Not exposed |

Rules, checked at compile time:

- A service is a non-static, non-abstract, non-generic class, public or
  internal.
- Methods are not generic, take no `ref`/`out`/`in` parameters, and are not
  overloaded (JavaScript cannot tell overloads apart).
- Methods inherited from base classes are exposed too; a method of a
  derived class hides the base's methods of its name.

### Calls run on the thread pool

Pages never wait on the UI thread: each call runs on the thread pool, so a
slow method does not freeze the window. Use the usual synchronization when
methods share state, and `OrielApp.Dispatch` to do something on the UI thread.

A service can ask for another policy with `Concurrency`:

```csharp
[Service(Concurrency = ServiceConcurrency.Serial)]
public class Cart
{
    private readonly List<string> _items = []; // no lock: one call at a time

    public void Add(string item) => _items.Add(item);
}
```

| `ServiceConcurrency` | Calls |
|---|---|
| `Parallel` (default) | Run on the thread pool, several at once. |
| `Serial` | Run one at a time, in the order they arrived, from all windows. An `await` inside a call keeps the next one waiting. |
| `UiThread` | Run on the UI thread, and so do their continuations after an `await`. Keep them short: the windows wait meanwhile. |

Cancellation works the same under each: a serial call whose page went away
while it waited is not run. Events are not affected.

### Parameters Oriel passes

Some parameter types are not sent by pages but passed by Oriel; they are left
out of the TypeScript signature:

| Type | Value |
|---|---|
| `CancellationToken` | Canceled when the calling page goes away: navigation, reload, or its window closing |
| `OrielWindow` | The window of the calling page |

```csharp
public async Task<string> Export(Deck deck, OrielWindow window, CancellationToken cancellation)
```

```ts
export(deck: Deck): Promise<string>
```

### Optional parameters

Parameters with default values are optional in TypeScript. A page can leave
them out, or pass `undefined`; in both cases the C# default is used.

```csharp
public List<string> Search(string query, int limit = 10, Theme? theme = null)
```

```ts
search(query: string, limit?: number, theme?: Theme | null): Promise<string[]>

Decks.search("q");                    // limit = 10, theme = null
Decks.search("q", undefined, "dark"); // limit = 10
```

JSON has no `undefined`: an argument left out between others is sent as
`null`, which C# reads as "left out" when the parameter cannot be null.

## Types

| C# | TypeScript | JSON |
|---|---|---|
| `string`, `char` | `string` | string |
| `bool` | `boolean` | boolean |
| `byte` … `ulong`, `float`, `double`, `decimal` | `number` | number (`NaN` and infinities become `null`) |
| `Guid` | `string` | `"6f9619ff-..."` |
| `DateTime`, `DateTimeOffset` | `string` | ISO 8601: `new Date(value)` reads it |
| `byte[]` | `string` | base64 |
| `JsonElement` | `unknown` | any JSON, as is |
| `T?` (value or reference) | `T \| null` | `null` or the value |
| `T[]`, `List<T>`, `IList<T>`, `IReadOnlyList<T>`, `IEnumerable<T>`… | `T[]` | array |
| `Dictionary<string, T>`, `IReadOnlyDictionary<string, T>` | `Record<string, T>` | object |
| An enum | A union of its names: `"light" \| "dark"` | the name, as a string |
| A class, record or struct | An `interface` of its public properties | object |

A JavaScript string may hold a lone surrogate, half of an emoji left by
cutting a text with `slice`; .NET reads no string from it. A call with
one fails as an argument that does not fit ("expected a string, got one
with a lone surrogate"), in a string or in a property name.

Names are camelCase in JSON and TypeScript (`URLPath` becomes `urlPath`),
as System.Text.Json writes them. The usual System.Text.Json attributes are
honored:

- `[JsonPropertyName("item_id")]` renames a property;
- `[JsonIgnore]` leaves one out;
- `[JsonStringEnumMemberName("lo")]` renames an enum member;
- `[JsonConstructor]` picks the constructor objects are read with.

### Objects

Objects are written with all their public readable properties, and read
back through:

1. the constructor marked `[JsonConstructor]`, else the parameterless one,
   else the only public one (a record's primary constructor);
2. then the properties left: public setters, and `init` setters, which are
   set without reflection.

A property that is missing from the JSON keeps its default value, except
that `required` properties and constructor parameters without a default must
be there. Properties without a setter are `readonly` in TypeScript.

### What is not supported (yet)

The generator reports these as errors, where they are used (see
[diagnostics.md](diagnostics.md)):

- interfaces and abstract classes (no polymorphism);
- generic classes and records of your own;
- tuples (use a record: its properties have names);
- `[Flags]` enums;
- dictionaries with keys that are not strings;
- multidimensional arrays;
- streams (`IAsyncEnumerable<T>`): planned.

## Events

An `Event<T>` property sends payloads to every page of the app:

```csharp
[Service]
public sealed class Clock
{
    public Clock() => _timer = new Timer(_ => Tick.Emit(DateTimeOffset.Now), null, 0, 1000);
    private readonly Timer _timer;

    /// <summary>Fires every second with the time.</summary>
    public Event<DateTimeOffset> Tick { get; } = new();
}
```

```ts
const stop = Clock.onTick((now) => (clock.textContent = new Date(now).toLocaleTimeString()));
// later
stop();
```

`Emit` can be called from any thread. It does nothing until the service is
bound, and reaches the pages that have loaded.

## Errors

A method that throws rejects the page's promise with a `CallError`. So do
calls whose arguments do not fit, with the path of the value that did not:

```
argument 'deck': $.slides[2].title: expected a string, got a number
```

### Errors pages can tell apart

Mark an exception with `[ServiceError("code")]`: pages get its code, and its
public properties as `data`.

```csharp
[ServiceError("conflict")]
public class ConflictException(string path, int version) : Exception($"{path} changed on disk")
{
    public string Path { get; } = path;
    public int Version { get; } = version;
}
```

```ts
import { CallError, Decks } from "./oriel";

try {
  await Decks.save(path, deck);
} catch (err) {
  if (err instanceof CallError && err.code === "conflict") {
    reload(err.data.path, err.data.version); // err.data is a ConflictException
  } else if (err instanceof CallError) {
    showError(err.message);
  }
}
```

The client's `CallError` is a union of the app's errors, by code: checking
`err.code` gives `err.data` its type. Other errors have a `code` and `data`
of `null`. Exceptions derived from a `[ServiceError]` one have its code.

### Messages of unexpected exceptions

Other exceptions may tell about the app's internals in their messages: file
paths, SQL. Outside development, pages get `"Service.Method failed"`
instead. In development, and for calls that did not fit (wrong arguments,
unknown methods), they get the message.

`OrielApp.CallFailed` is raised in C# for every failed call, with the whole
exception, for logging.

## The TypeScript client

The client is one self-contained module with no dependencies: it talks to the
page runtime Oriel injects into its windows (`window.oriel`). It is written
after each build to the path set by `OrielTypeScriptOutput`, only when it
changed. To write it by hand, run the app with `ORIEL_GENERATE=path/oriel.ts`:
`OrielApp.Run` then writes the client and returns at once.

Commit it with the frontend, so the page type-checks without building the C#
side.

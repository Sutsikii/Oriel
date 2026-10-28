# npm packages

A generated client needs nothing but the page runtime Oriel injects. The
npm packages are for what comes around it: a runtime clients can share,
and hooks for frameworks.

| Package | What it gives |
|---|---|
| `oriel-runtime` | The client's runtime: `call`, `on`, `stream`, `CallErrorBase`, the types of `window.oriel` |
| `oriel-react` | `useOrielEvent` and `useOrielQuery` |
| `oriel-vue` | `useOrielEvent` and `useOrielQuery`, as composables |
| `oriel-tanstack-query` | `invalidateOn`: C# events invalidate the queries of what they changed |

## The runtime

With `OrielTypeScriptRuntime` set to `oriel-runtime` in the app's project,
the generated client imports its runtime from the package rather than
carrying it:

```xml
<OrielTypeScriptRuntime>oriel-runtime</OrielTypeScriptRuntime>
```

```sh
npm install oriel-runtime
```

Without it, nothing changes: each client carries its runtime. Either way,
`CallError` is one class for every client and package, so
`err instanceof CallError` holds whichever threw.

## React

```tsx
import { useOrielEvent, useOrielQuery } from "oriel-react";
import { Decks } from "./oriel";

function DeckView({ path }: { path: string }) {
  // Called again when path changes, and when C# fires Decks.Changed.
  const deck = useOrielQuery(() => Decks.open(path), [path], { refreshOn: [Decks.onChanged] });
  // Listened to while mounted.
  useOrielEvent(Decks.onSaved, (saved) => toast(`${saved.title} saved`));
  if (deck.error) return <p>{String(deck.error)}</p>;
  return deck.loading ? <Spinner /> : <Slides deck={deck.data!} />;
}
```

- `useOrielEvent(subscribe, listener)` takes the client's `onEvent`
  function, and stops listening when the component unmounts. The listener
  may change on each render.
- `useOrielQuery(query, deps, { refreshOn })` gives `data`, `error`,
  `loading` and `refresh()`. It calls again when `deps` change, when
  `refresh()` is called and when an event of `refreshOn` fires; the result
  of a call overtaken by a newer one is dropped.

[examples/tasks-react](../examples/tasks-react) uses both.

## Vue

```ts
import { useOrielEvent, useOrielQuery } from "oriel-vue";
import { Decks } from "./oriel";

const path = ref("/decks/q3.deck");
const deck = useOrielQuery(() => Decks.open(path.value), { watch: path, refreshOn: [Decks.onChanged] });
useOrielEvent(Decks.onSaved, (saved) => toast(`${saved.title} saved`));
```

`data`, `error` and `loading` are refs. Both stop listening when the
component, or the effect scope, goes.

## TanStack Query

TanStack Query caches calls; C# events say when what they cached changed:

```ts
import { invalidateOn, invalidateOnPayload } from "oriel-tanstack-query";

const decks = useQuery({ queryKey: ["decks"], queryFn: () => Decks.list() });
useEffect(() => invalidateOn(queryClient, Decks.onChanged, ["decks"]), []);
// Or the key of one item, from the event's payload:
useEffect(() => invalidateOnPayload(queryClient, Decks.onSaved, (deck) => ["deck", deck.id]), []);
```

It works with the QueryClient of any framework: it only calls
`invalidateQueries`.

## Publishing

The packages are in [packages/](../packages), built and tested together
(`npm test` there), and published by the release workflow at the version
of the release's tag, with npm's provenance. It needs the repository's
`NPM_TOKEN` secret, an npm token allowed to publish them.

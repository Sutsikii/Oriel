# oriel-react

React hooks for [Oriel](https://github.com/Sutsikii/Oriel)'s generated clients.

```tsx
import { useOrielEvent, useOrielQuery } from "oriel-react";
import { Decks } from "./oriel";

function DeckView({ path }: { path: string }) {
  const deck = useOrielQuery(() => Decks.open(path), [path], { refreshOn: [Decks.onChanged] });
  useOrielEvent(Decks.onSaved, (saved) => toast(`${saved.title} saved`));
  return deck.loading ? <Spinner /> : <Slides deck={deck.data!} />;
}
```

See [packages.md](https://github.com/Sutsikii/Oriel/blob/main/docs/packages.md).

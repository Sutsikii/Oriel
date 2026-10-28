# oriel-vue

Vue composables for [Oriel](https://github.com/Sutsikii/Oriel)'s generated clients.

```ts
import { useOrielEvent, useOrielQuery } from "oriel-vue";
import { Decks } from "./oriel";

const deck = useOrielQuery(() => Decks.open(path.value), { watch: path, refreshOn: [Decks.onChanged] });
useOrielEvent(Decks.onSaved, (saved) => toast(`${saved.title} saved`));
```

See [packages.md](https://github.com/Sutsikii/Oriel/blob/main/docs/packages.md).

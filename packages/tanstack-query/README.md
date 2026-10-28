# oriel-tanstack-query

[TanStack Query](https://tanstack.com/query) and [Oriel](https://github.com/Sutsikii/Oriel): C# events invalidate the queries of what they changed, so pages refetch.

```ts
import { invalidateOn } from "oriel-tanstack-query";

const decks = useQuery({ queryKey: ["decks"], queryFn: () => Decks.list() });
useEffect(() => invalidateOn(queryClient, Decks.onChanged, ["decks"]), []);
```

See [packages.md](https://github.com/Sutsikii/Oriel/blob/main/docs/packages.md).

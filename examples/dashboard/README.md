# Dashboard

A real-time dashboard: the app's metrics, and a log.

- `Metrics.Sampled` fires 120 times a second, as a coalesced event
  (`Coalesce = true`): the page gets the latest sample when it is ready,
  and draws once per frame however many came; hidden, it falls behind by
  nothing ([bindings.md](../../docs/bindings.md#coalesced-events)).
- `Metrics.log()` is a stream (`IAsyncEnumerable<LogEntry>`) the page reads
  with `for await`. Pausing breaks the loop, which cancels the stream in
  C#; it never runs further ahead than the page reads
  ([bindings.md](../../docs/bindings.md#streams)).

```sh
cd frontend
npm install
npm run dev
```

`dotnet publish -c Release -r win-x64` gives a native executable, as CI does.

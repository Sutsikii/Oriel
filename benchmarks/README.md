# Benchmarks

What a call from a page costs, so that optimizations are measured rather
than guessed. Baseline numbers are in
[docs/architecture.md](../docs/architecture.md#what-a-call-costs).

| Measure | What it covers |
|---|---|
| `DispatchAdd` | the generated dispatcher of a call of two `int`s: reading them, calling, writing the result |
| `DispatchDeck` | the same with a 3 KB record of 20 slides, read and written by the generated codecs |
| `ProtocolAdd`, `ProtocolDeck` | `PageConnection.ReceiveAsync`: a page's message parsed, run on the thread pool, the reply encoded |
| `EncodeEvent` | an event's message, as `Event<T>.Emit` encodes it once for every page |
| `RoundTrip` | a page in a real window: the latency of calls one after another (p50, p99), then calls a second with 10 000 in flight |

## Run them

```sh
cd benchmarks/Oriel.Benchmarks
dotnet run -c Release -- --filter * --job short --artifacts ../../benchmark-results
cd ../..
dotnet run --project benchmarks/Oriel.Benchmarks -c Release -- roundtrip benchmark-results/roundtrip.json
dotnet run --project benchmarks/Oriel.Benchmarks -c Release -- check benchmark-results benchmark-results/roundtrip.json benchmarks/baseline.json
```

BenchmarkDotNet takes its usual arguments (`--filter *Deck*`, or no
`--job short` for longer and steadier runs). `check` prints each measure
against the baseline, and fails when one is worse than its baseline times
`factor`: a time longer, or fewer calls a second. p99 is reported only,
since it is too noisy on shared machines.

## In CI

The Benchmarks workflow runs them on each push to `main`, on pull
requests that change `src/` or `benchmarks/`, and on demand. The table goes
to the job's summary, and the reports to the `benchmark-results` artifact.

The baseline is CI's own: the numbers of a `windows-latest` runner, 2 to
4 times slower than a developer machine. Runners are noisy, hence the
factor of 3: it catches a call that became several times slower, not a
few percent. After a change that makes calls faster or slower on purpose,
update the baseline from the run's `benchmark-results` artifact:

```sh
gh run download <run id> -n benchmark-results -D ci-results
dotnet run --project benchmarks/Oriel.Benchmarks -c Release -- check ci-results ci-results/roundtrip.json benchmarks/baseline.json --update
```

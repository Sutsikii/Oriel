<script lang="ts">
  import { onDestroy } from "svelte";
  import { CallError, Greeter } from "./oriel"; // generated from Services.cs on each build

  let name = $state("");
  let greeting = $state<{ text: string; error: boolean } | null>(null);
  let log = $state<string[]>([]);

  // A call: typed arguments and result; C# exceptions arrive as CallError.
  async function greet(e: SubmitEvent) {
    e.preventDefault();
    try {
      greeting = { text: (await Greeter.greet(name)).text, error: false };
    } catch (err) {
      greeting = { text: err instanceof CallError ? err.message : String(err), error: true };
    }
  }

  // An event, fired in C#.
  onDestroy(Greeter.onGreeted((who) => log.unshift(`Greeted ${who}`)));
</script>

<main>
  <h1>OrielTemplate</h1>
  <form onsubmit={greet}>
    <input bind:value={name} autocomplete="off" placeholder="Your name" />
    <button type="submit">Greet</button>
  </form>
  <output class:error={greeting?.error}>{greeting?.text}</output>
  <ul>
    {#each log as item, i (log.length - i)}
      <li>{item}</li>
    {/each}
  </ul>
</main>

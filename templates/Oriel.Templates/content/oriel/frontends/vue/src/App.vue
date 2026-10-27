<script setup lang="ts">
import { onUnmounted, ref } from "vue";
import { CallError, Greeter } from "./oriel"; // generated from Services.cs on each build

const name = ref("");
const greeting = ref<{ text: string; error: boolean } | null>(null);
const log = ref<string[]>([]);

// A call: typed arguments and result; C# exceptions arrive as CallError.
async function greet() {
  try {
    greeting.value = { text: (await Greeter.greet(name.value)).text, error: false };
  } catch (err) {
    greeting.value = { text: err instanceof CallError ? err.message : String(err), error: true };
  }
}

// An event, fired in C#.
const stop = Greeter.onGreeted((who) => log.value.unshift(`Greeted ${who}`));
onUnmounted(stop);
</script>

<template>
  <main>
    <h1>OrielTemplate</h1>
    <form @submit.prevent="greet">
      <input v-model="name" autocomplete="off" placeholder="Your name" autofocus />
      <button type="submit">Greet</button>
    </form>
    <output role="status" :class="{ error: greeting?.error }">{{ greeting?.text }}</output>
    <ul>
      <li v-for="(item, i) in log" :key="log.length - i">{{ item }}</li>
    </ul>
  </main>
</template>

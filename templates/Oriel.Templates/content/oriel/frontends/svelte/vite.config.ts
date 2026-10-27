import { defineConfig } from "vite";
import { svelte } from "@sveltejs/vite-plugin-svelte";

// The app serves the built files at https://app.oriel/, and `npm run dev`
// points it at this server instead.
export default defineConfig({
  plugins: [svelte()],
  server: { port: 5173, strictPort: true },
  build: { outDir: "dist", emptyOutDir: true },
});

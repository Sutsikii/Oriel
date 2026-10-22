import { defineConfig } from "vite";

// The app serves the built files at https://app.oriel/, and `npm run dev`
// points it at this server instead.
export default defineConfig({
  server: { port: 5173, strictPort: true },
  // three.js, about 580 kB, is a chunk of its own, loaded when the 3D view is first shown.
  build: { outDir: "dist", emptyOutDir: true, chunkSizeWarningLimit: 700 },
});

import { defineConfig } from "vite";

// The app serves the built files at https://app.oriel/, and `npm run dev`
// points it at this server instead.
export default defineConfig({
  server: { port: 5173, strictPort: true },
  // three.js is one chunk of about 700 kB, read from the disk.
  build: { outDir: "dist", emptyOutDir: true, chunkSizeWarningLimit: 1000 },
});

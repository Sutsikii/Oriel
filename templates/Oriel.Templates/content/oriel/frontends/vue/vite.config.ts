import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

// The app serves the built files at https://app.oriel/, and `npm run dev`
// points it at this server instead.
export default defineConfig({
  plugins: [vue()],
  server: { port: 5173, strictPort: true },
  build: { outDir: "dist", emptyOutDir: true },
});

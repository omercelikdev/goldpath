import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

export default defineConfig({
  // RELATIVE asset paths: the console is served under whatever prefix the adopter chose
  // (`/goldpath/console` by default, but it is theirs to change), so an absolute "/assets"
  // would 404 everywhere except the root.
  base: "./",
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@goldpath/kit": fileURLToPath(new URL("../kit/src/index.ts", import.meta.url)) },
  },
});

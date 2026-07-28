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
    alias: {
      "@goldpath/kit": fileURLToPath(new URL("../kit/src/index.ts", import.meta.url)),
      // kit and console are separate pnpm stores; lucide loaded from the KIT's store
      // binds the kit's React instance while react-dom renders with the console's —
      // same versions, different identities, invalid-hook-call. Pin lucide to ONE copy.
      "lucide-react": fileURLToPath(new URL("./node_modules/lucide-react", import.meta.url)),
    },
    // The kit is consumed as SOURCE (the alias above), so its imports must resolve to the
    // console's single React — two copies is the classic invalid-hook-call.
    dedupe: ["react", "react-dom", "lucide-react"],
  },
});

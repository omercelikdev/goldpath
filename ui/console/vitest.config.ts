import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // kit and console are SEPARATE pnpm stores: lucide required from the kit's store
      // binds the kit's React instance while react-dom renders with the console's —
      // same version, two identities, invalid-hook-call. One physical copy, always.
      "lucide-react": fileURLToPath(new URL("./node_modules/lucide-react", import.meta.url)),
      "@radix-ui/react-dialog": fileURLToPath(new URL("./node_modules/@radix-ui/react-dialog", import.meta.url)),
      "@radix-ui/react-dropdown-menu": fileURLToPath(new URL("./node_modules/@radix-ui/react-dropdown-menu", import.meta.url)),
      "cmdk": fileURLToPath(new URL("./node_modules/cmdk", import.meta.url)),
      "@radix-ui/react-tooltip": fileURLToPath(new URL("./node_modules/@radix-ui/react-tooltip", import.meta.url)),
    },
    dedupe: ["react", "react-dom"],
  },
  test: {
    environment: "jsdom",
    server: {
      // Externalized, lucide bypasses the alias above; inlined, it resolves through it.
      deps: { inline: ["lucide-react", "cmdk", /@radix-ui/] },
    },
    coverage: {
      // Only the SOURCE is judged: the gallery and the dev entry point are worked
      // examples, and a barrel file has nothing to test.
      include: ["src/**/*.{ts,tsx}"],
      exclude: ["src/index.ts", "src/test-setup.ts", "src/**/*.{test,spec}.{ts,tsx}"],
    },

    globals: true,
    setupFiles: ["./src/test-setup.ts"],
    // The e2e suite belongs to Playwright (`pnpm e2e`); vitest owns src/ only.
    include: ["src/**/*.{test,spec}.{ts,tsx}"],
  },
});

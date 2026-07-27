import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { "@goldpath/kit": fileURLToPath(new URL("../kit/src/index.ts", import.meta.url)) },
  },
  test: {
    environment: "jsdom",
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

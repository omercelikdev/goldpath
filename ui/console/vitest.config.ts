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
    globals: true,
    setupFiles: ["./src/test-setup.ts"],
    // The e2e suite belongs to Playwright (`pnpm e2e`); vitest owns src/ only.
    include: ["src/**/*.{test,spec}.{ts,tsx}"],
  },
});

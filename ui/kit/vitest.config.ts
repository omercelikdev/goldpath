import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
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
  },
});

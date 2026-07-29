import "@testing-library/jest-dom/vitest";

// jsdom ships neither ResizeObserver nor scrollIntoView; cmdk uses both. No-ops are
// enough — nothing asserts on measured heights or scroll positions.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};
Element.prototype.scrollIntoView ??= () => {};

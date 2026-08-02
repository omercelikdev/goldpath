import "@testing-library/jest-dom/vitest";

// jsdom ships neither ResizeObserver nor scrollIntoView; cmdk uses both. No-ops are
// enough — nothing asserts on measured heights or scroll positions.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};
Element.prototype.scrollIntoView ??= () => {};

// Radix Select drives pointer capture; jsdom has none of it. No-ops suffice.
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.setPointerCapture ??= () => {};
Element.prototype.releasePointerCapture ??= () => {};

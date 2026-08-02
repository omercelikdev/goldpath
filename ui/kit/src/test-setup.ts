import "@testing-library/jest-dom/vitest";

// jsdom ships no ResizeObserver; cmdk observes its list to size the group headings.
// A no-op observer is enough — nothing in a test asserts on measured heights.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};

// jsdom also lacks scrollIntoView, which cmdk calls to keep the selection visible.
Element.prototype.scrollIntoView ??= () => {};

// Radix Select drives pointer capture; jsdom has none of it. No-ops suffice.
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.setPointerCapture ??= () => {};
Element.prototype.releasePointerCapture ??= () => {};

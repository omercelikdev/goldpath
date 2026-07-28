import { describe, expect, it } from "vitest";
import { shortStamp } from "./stamp";

describe("shortStamp", () => {
  it("quiets an ISO stamp to date + wall-clock seconds", () => {
    expect(shortStamp("2026-07-28T19:48:50.027543+00:00")).toBe("2026-07-28 19:48:50");
    expect(shortStamp("2026-07-28T19:48:50Z")).toBe("2026-07-28 19:48:50");
  });

  it("never parses through Date — the server's wall clock survives verbatim", () => {
    // A +03:00 stamp keeps ITS OWN clock reading: shifting it into the viewer's zone
    // would disagree with the server logs an incident is read against.
    expect(shortStamp("2026-07-28T22:48:50+03:00")).toBe("2026-07-28 22:48:50");
  });

  it("hands back what it cannot read, and a dash for nothing", () => {
    expect(shortStamp("yesterday-ish")).toBe("yesterday-ish");
    expect(shortStamp(null)).toBe("—");
    expect(shortStamp(undefined)).toBe("—");
  });
});

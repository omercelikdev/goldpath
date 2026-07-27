import { humanizeSeconds } from "./duration";

describe("the console's one duration vocabulary", () => {
  it("reads seconds below the minute-and-a-half boundary", () => {
    expect(humanizeSeconds(0)).toBe("0s");
    expect(humanizeSeconds(89)).toBe("89s");
  });

  it("switches to minutes, then hours, then days", () => {
    expect(humanizeSeconds(90)).toBe("2m");
    expect(humanizeSeconds(7_200)).toBe("2h");
    expect(humanizeSeconds(172_800)).toBe("2d");
  });

  it("never renders a negative age — a clock skew is not a time machine", () => {
    expect(humanizeSeconds(-5)).toBe("0s");
  });
});

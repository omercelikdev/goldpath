# skills/

The claim below used to be unprotected. `scripts/skills-parity.sh` now checks it: the files
that must be byte-identical across the two templates and CorPay are, and the five that
deliberately differ are listed there with the reason each one does. It was written the day
api-portal's `stop-gate.sh` was found already drifted from the template's.

Empty **by design** (goldpath-skills-v1 D2): the shipped skill layer lives inside the
template — `templates/goldpath-solution/.claude/skills/` — and is packed with it, so every
generated app is born with the skills (CorPay carries a byte-identical copy). Field status
per skill: `docs/strategy/ai-sdlc-status.md` §2.

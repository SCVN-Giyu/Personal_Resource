# WorkLog: Playable Doubt & Chaos Runner (`playable-doubt-driven-development`)

```yaml
meta:
  spec_doc: ../Specs/Spec_PlayableDoubtChaos.md
  active_focus: browser-chaos-runner.live-calibration
  updated: 2026-11-10
```

## Tree Overview
```text
PlayableDoubtChaos
├── static-chaos-contract [done]
├── browser-chaos-runner [iterating]
│   ├── seeded-prng-and-timewarp [done]
│   ├── input-and-viewport-monkey [done]
│   ├── flight-recorder-and-mcp [done]
│   └── live-calibration [in-progress]  <-- ACTIVE FOCUS
├── luna-canvas-calibration [pending]
├── state-canary-bridge [pending]
└── live-smoke-verification [pending]
```

---

## Nodes

### `static-chaos-contract` {#static-chaos-contract}
```yaml
spec_ref: "Spec#1 Adversarial Code Review & Chaos Contract"
state: done
scripts:
  - Agent_Skills/gemini/playable-doubt-driven-development/SKILL.md
  - Agent_Skills/gemini/playable-doubt-driven-development/references/playable-chaos-contract.md
  - Agent_Workflow_Tree/Workflow/Adversarial_And_Chaos.md
contracts: "CLAIM -> EXTRACT -> DOUBT -> RECONCILE -> STOP across 6 Unity/Luna chaos vectors"
solution: "Split general doubt-driven-development from playable-doubt-driven-development; removed noisy external CLI prompts; wired into auto-trigger tree"
constraints: "Never enter Unity Play Mode or trigger builds without explicit user permission; respect C# <= 7.3 and active JS physics adapter parity"
```

### `browser-chaos-runner` {#browser-chaos-runner}
```yaml
spec_ref: "Spec#2 Seeded Luna Browser Chaos Runner (run_luna_chaos.mjs)"
state: iterating
├── seeded-prng-and-timewarp:
│     state: done
│     scripts: [Agent_Skills/gemini/playable-doubt-driven-development/scripts/run_luna_chaos.mjs]
│     contracts: "Overrides Math.random with Mulberry32(seed); exposes window.__CHAOS_ADVANCE_TIME__(deltaMs) wrapping performance.now() and Date.now()"
│     solution: "Pre-boot injection via page.addInitScript before Luna bundle executes"
│
├── input-and-viewport-monkey:
│     state: done
│     scripts: [Agent_Skills/gemini/playable-doubt-driven-development/scripts/run_luna_chaos.mjs]
│     contracts: "Executes deterministic sequences of multi-tap bursts, pointer drags (25% mid-drag drop),dt lag spikes (200-450ms), and 390x844 <-> 844x390 orientation flips"
│
├── flight-recorder-and-mcp:
│     state: done
│     scripts:
│       - Agent_Skills/gemini/playable-doubt-driven-development/scripts/run_luna_chaos.mjs
│       - Agent_Skills/gemini/agent-stack-setup/scripts/setup_agent_stack.py
│     contracts: "Writes failure_seed_<N>.json + failure_seed_<N>.png + exact --seed <N> --headed replay command; @playwright/mcp wired across Gemini, Cursor, Codex"
│
└── live-calibration:  # <-- ACTIVE FOCUS
      state: in-progress
      scripts: [Agent_Skills/gemini/playable-doubt-driven-development/scripts/run_luna_chaos.mjs]
      problem: "Initial v1 runner is built and syntax-verified, but has not yet been battle-tested and tuned against a real exported Luna HTML playable build to refine canvas hit-targeting, rAF clock driving, and game-specific invariant hooks"
      solution: "Scaffolded CLI + init hooks; ready for first live test against a real Luna HTML build in next session"
      constraints: "Must run standalone via node without polluting target Unity project dependencies; must catch rare timing/input bugs with 100% deterministic seed replay"
      next: "Run run_luna_chaos.mjs against an actual exported Luna HTML build, observe where blind pointer coordinates or rAF timestamps miss Luna's internal loop, and refine the runner"
```

### `luna-canvas-calibration` {#luna-canvas-calibration}
```yaml
spec_ref: "Spec#3 Real Luna Build Calibration & Canvas Coordinate Mapping"
state: pending
problem: "Blind random x/y clicks may miss interactive objects or Luna's letterboxed <canvas> bounds; requestAnimationFrame(timestamp) may also need virtual timestamp wrapping alongside performance.now()"
constraints: "Must support both single-file HTML builds and LunaTemp/stage3/develop/ directory builds"
next: "Inspect Luna's WebGL canvas bounding box and rAF timestamp handling during live run; add smart canvas-bounded targeting and rAF timestamp virtualization if needed"
```

### `state-canary-bridge` {#state-canary-bridge}
```yaml
spec_ref: "Spec#4 Automated C#/Bridge State Canary (__CHAOS_INVARIANTS__)"
state: pending
problem: "Silent softlocks (e.g. stuck transition lock, negative target counter) don't throw JS errors unless window.__CHAOS_INVARIANTS__ is exposed"
constraints: "Must compile cleanly under Luna Bridge C# 7.3 without heavy boilerplate"
next: "Design a tiny reusable C# helper or Bridge attribute pattern that exposes coordinator invariants to window.__CHAOS_INVARIANTS__ automatically in develop builds"
```

### `live-smoke-verification` {#live-smoke-verification}
```yaml
spec_ref: "Spec#5 End-to-End Verification & Smoke Test Suite"
state: pending
next: "Verify seed discovery + deterministic --seed <N> --headed replay on a real rare bug in a Luna playable build"
```

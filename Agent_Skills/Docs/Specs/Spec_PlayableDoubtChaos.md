# Spec: Playable Doubt & Chaos Runner (`playable-doubt-driven-development`)
> worklog: [WorkLog_PlayableDoubtChaos.md](../WorkLogs/WorkLog_PlayableDoubtChaos.md)

## Requirements Checklist
- [x] **1. Adversarial Code Review & Chaos Contract** — 6-vector Unity/Luna static doubt protocol & portable routing -> [WorkLog#static-chaos-contract](../WorkLogs/WorkLog_PlayableDoubtChaos.md#static-chaos-contract)
- [~] **2. Seeded Luna Browser Chaos Runner (`run_luna_chaos.mjs`)** — Deterministic PRNG, virtual time-warp spikes, input/orientation monkey, flight recorder -> [WorkLog#browser-chaos-runner](../WorkLogs/WorkLog_PlayableDoubtChaos.md#browser-chaos-runner)
- [ ] **3. Real Luna Build Calibration & Canvas Coordinate Mapping** — Verify pointer dispatch against real Luna WebGL canvas & MRAID/NUC lifecycle hooks -> [WorkLog#luna-canvas-calibration](../WorkLogs/WorkLog_PlayableDoubtChaos.md#luna-canvas-calibration)
- [ ] **4. Automated C#/Bridge State Canary (`__CHAOS_INVARIANTS__`)** — Auto-inject or bridge C# state invariants into JS to catch silent softlocks without manual wiring -> [WorkLog#state-canary-bridge](../WorkLogs/WorkLog_PlayableDoubtChaos.md#state-canary-bridge)
- [ ] **5. End-to-End Verification & Smoke Test Suite** — Live test on actual exported Luna playable builds to prove 99% rare-bug coverage -> [WorkLog#live-smoke-verification](../WorkLogs/WorkLog_PlayableDoubtChaos.md#live-smoke-verification)

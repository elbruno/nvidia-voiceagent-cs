# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture, scope, priorities | Dallas | Project structure, decisions, trade-offs |
| ASP.NET Core, WebSockets, API | Ripley | Server code, endpoints, WebSocket handlers |
| NVIDIA models, ML integration | Parker | TorchSharp, ONNX, Parakeet, FastPitch, HiFiGAN |
| Testing, quality, edge cases | Lambert | Unit tests, integration tests, validation |
| Code review | Dallas | Review PRs, check quality, suggest improvements |
| Session logging | Scribe | Automatic — never needs routing |

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.

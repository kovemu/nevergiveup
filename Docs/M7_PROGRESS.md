# M7 progress

Updated 2026-10-24. See [M7-A handoff](M7A_HANDOFF.md) for results and limits.
The latest reduced-scope request supersedes the macro-policy and training-only plans.

M6.2 baseline 7839718; original M7 specification 92b6eab.
Seven real Play Mode tempo checks pass. Walk 2.8, turn 150, preparation .4/.65,
landing .35, jump horizontal 6.4 and heights 1.55/3 preserved.

ML-Agents 4.1.0 / Inference 2.6.1 installed with Client.Add.
Python 3.10.12 / Torch 2.2.1 CUDA ran on RTX 3060.
Flat, box and bounded platform training completed. Final independent evaluation
passed 128/128 per simple terrain category. No further training is planned.

Actual Editor Prologue inference, fall/recovery, moved-box observations,
missing-model fallback, reactivation and no-progress fallback verified.
M5 input/memory and M6.2 arc checks passed. Full-stage learned success not claimed.
Original checkpoints, logs, user layout edits and M4 previews remain preserved.
Ground-only speed multiplier retains existing jump reach and JSON map identity.

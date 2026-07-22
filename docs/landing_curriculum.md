# Continuous landing curricula

`Chopstick Catch Landing` and `Falcon 9 Leg Landing` are separate scenarios with separate curriculum progress/counters. Both use one normalized episode difficulty `d` in `[0, 1]`. Spawn ranges and success tolerances interpolate continuously; there is no easy/hard stage switch and physical behavior does not suddenly appear at a threshold.

## Comparison modes

- `Adaptive` advances above 80% recent success, retreats below 50%, and holds within that hysteresis band.
- `Monotonic` uses the same promotion rule but never moves backward.
- `Fixed Full Difficulty` holds `d = 1` and is the no-curriculum/evaluation condition.

Changing mode resets the active scenario's curriculum counters so one treatment cannot inherit another's progress. Chopstick and leg curricula never share progress.

## Adaptive safeguards

For the first 32 episodes, mastery uses cumulative success; afterwards it uses an exponential moving average. Difficulty updates only after one batch equal to the number of active parallel areas. The default change is capped at 0.02 linear difficulty per batch, retreat is half as fast as promotion, and adaptive progress cannot retreat more than 0.20 below its previous peak.

Fifteen percent of training episodes replay a complete profile at `max(0, d - 0.20)`. “Complete” means spawn conditions, success limits, geometry limits, and stable-hold time all come from the easier profile. Replay episodes train PPO but do not update mastery, so they cannot promote the curriculum. Each agent freezes its selected profile for the whole episode.

Telemetry stores global difficulty, actual episode difficulty, and the replay flag independently.

## Shared continuous profile

The built-in endpoints are:

| Quantity | `d = 0` | `d = 1` |
|---|---:|---:|
| Spawn altitude | 120–220 m | 300–1000 m |
| Horizontal offset radius | 8 m | 100 m |
| Downward speed range | 5–20 m/s | 20–120 m/s |
| Horizontal speed max | 1 m/s | 25 m/s |
| Pitch/roll range | 3 deg | 18 deg |
| Angular-speed max | 0 deg/s | 55 deg/s |
| Success radius | 8 m | 2 m |
| Total-speed max | 7 m/s | 2.5 m/s |
| Vertical-speed max | 5 m/s | 2 m/s |
| Horizontal-speed max | 5 m/s | 1 m/s |
| Tilt max | 20 deg | 5 deg |
| Angular-rate max | 50 deg/s | 25 deg/s |

The sampler couples altitude and velocity through conservative braking/lateral-control checks rather than sampling impossible combinations independently. For the leg-landing hardware comparison, this check uses one fixed single-engine Falcon reference envelope so every ablation receives the same seeded task distribution. Actual vehicle TWR is still validated/reported separately.

The flyaway ceiling is calculated per episode as the relevant landing frame's starting altitude plus 100 m. A horizontal error above 150 m is a separate flyaway condition.

## Chopstick catch behavior

Observations and reward use `CatchFrame` near the grid fins and the target at the configured catch altitude (60 m by default). The generated tower/arms are visual and logical only: they do not apply impulses, close, form joints, or support the vehicle.

Capture requires `CatchFrame` to stay inside the logical envelope while satisfying total/vertical/horizontal speed, tilt, angular-rate, uprightness, and yaw limits. Yaw tolerance interpolates from 30 deg to 10 deg, capture half-size from 8 m to 3 m, and stable hold from 0.15 s to 0.45 s.

## Leg-landing behavior

Observations and reward use `FeetFrame` at the generated landing-foot plane and the measured top of the existing landing-pad collider. Four physical legs are active for the whole scenario; the curriculum never switches collision on midway through training. Heading/yaw is not a requirement.

The pad half-size remains 10 m. Safe first contact must meet the current motion/attitude limits. Stable success additionally requires at least three feet on the pad, no outside-pad foot, no body/strut strike, position within the current success radius, and continuous compliance for a hold interpolated from 0.25 s to 1.0 s.

## Reward timing

Dense landing shaping is a per-second rate multiplied by `Time.fixedDeltaTime`. First-contact/stable milestones and terminal rewards are one-off events. All contributions use `AddReward`, so a terminal signal is added to shaping accumulated since the previous ML-Agents decision instead of erasing it.

Both landing models use signed goal closure, a gravity/height-derived descent profile, centering, uprightness, near-target velocity/rotation, control-effort, and time terms. Neither rewards a prescribed throttle setting or merely remaining alive. Chopstick adds yaw/capture logic; leg landing replaces it with physical first-contact and stable-support logic. See [reward_functions.md](reward_functions.md).

## PPO temporal settings

The experiment contract is a 0.01 s physics step and decision period 3: one decision every 0.03 s (about 33.3 Hz).

- `gamma = 0.995` discounts per policy decision. A reward 10 simulated seconds away retains about 19% of its value.
- `lambda = 0.98` is the Generalized Advantage Estimation bias/variance control. Higher values propagate observed outcomes farther back with more variance; lower values rely more on the critic with more bias.
- `time_horizon = 1024` is a rollout chunk, not an episode limit. It covers 30.72 simulated seconds. Longer episodes continue in another chunk and bootstrap the unfinished return from the critic.
- Curiosity is disabled for the primary dense-reward experiment so novelty reward is not an uncontrolled treatment.

## Standard evaluation

Every saved landing policy is evaluated deterministically on the same seeded 200-episode, fixed-full-difficulty suite. Standard evaluation forces clear air, density multiplier 1, no faults, no replay, and scenario-specific canonical geometry. Fixed hover uses the same evaluator infrastructure with its canonical near-target spawn distribution and natural fuel/failure endpoint. Both write full-rate telemetry, an episode CSV, and an aggregate JSON summary. See [evaluation_protocol.md](evaluation_protocol.md).

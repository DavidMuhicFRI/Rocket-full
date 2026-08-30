# Continuous landing curricula

`Chopstick Catch Landing` and `Falcon 9 Leg Landing` are separate scenarios with separate curriculum progress/counters. Both use one normalized episode difficulty `d` in `[0, 1]`. Spawn ranges and success tolerances interpolate continuously; there is no easy/hard stage switch.

## Comparison modes

- `Adaptive` advances above 80% recent success, retreats below 50%, and holds within that hysteresis band.
- `Monotonic` uses the same promotion rule but never moves backward.
- `Fixed Full Difficulty` holds `d = 1` for a no-curriculum training or manual-inference baseline. Standard Evaluation selects its own frozen difficulty bands independently of this mode.

Changing mode resets the active scenario's curriculum counters so one treatment cannot inherit another's progress. Chopstick and leg curricula never share progress.

## Adaptive safeguards

For the first 32 episodes, mastery uses cumulative success; afterwards it uses an exponential moving average. Difficulty updates only after one batch equal to the number of active parallel areas. The default change is capped at 0.02 linear difficulty per batch, retreat is half as fast as promotion, and adaptive progress cannot retreat more than 0.20 below its previous peak.

Fifteen percent of training episodes replay a complete profile at `max(0, d - 0.20)`. “Complete” means spawn conditions, success limits, geometry limits, and stable-hold time all come from the easier profile. Replay episodes train PPO but do not update mastery, so they cannot promote the curriculum. Each agent freezes its selected profile for the whole episode.

Telemetry stores global difficulty, actual episode difficulty, and the replay flag independently.

## Continuous profiles

Chopstick catch retains its broad acquisition profile. The spawn rows are curriculum defaults; success rows are the default objective's editable termination endpoints:

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

Physical leg landing uses a linearly interpolated terminal-descent profile that
starts with a real horizontal-navigation problem while keeping the first tasks
slow enough to learn controlled contact:

| Quantity | `d = 0` | `d = 1` |
|---|---:|---:|
| Spawn altitude | 120-180 m | 400-1000 m |
| Horizontal offset radius | 8 m | 60 m |
| Downward speed range | 10-18 m/s | 30-70 m/s |
| Horizontal speed max | 1 m/s | 12 m/s |
| Pitch/roll range | 1 deg | 8 deg |
| Angular-speed max | 2 deg/s | 12 deg/s |
| Success radius | 6 m | 1.5 m |
| Total-speed max | 7 m/s | 2 m/s |
| Vertical-speed max | 6 m/s | 1.5 m/s |
| Horizontal-speed max | 2 m/s | 0.75 m/s |
| Tilt max | 12 deg | 3 deg |
| Angular-rate max | 30 deg/s | 10 deg/s |
| Physical pad half-size | 25 m | 25 m |
| Minimum supporting feet | 4 | 4 |
| Stable hold | 0.40 s | 1.0 s |

The sampler couples altitude and velocity through conservative braking/lateral-control checks rather than sampling impossible combinations independently. Both landing tasks derive this envelope from the selected vehicle's current mass, active engines, gimbal authority, and startup delay, so custom vehicles are not assigned obviously unreachable starts.

In the default leg objective, the altitude-escape rule allows 100 m above the
episode's starting landing-frame altitude, the time limit is 60 s, and the
planar-flyaway rule allows 150 m of horizontal error. All rules and thresholds
remain configurable.

## Chopstick catch behavior

Observations and reward use `CatchFrame` near the grid fins and the target at the configured catch altitude (60 m by default). The generated tower/arms are visual and logical only: they do not apply impulses, close, form joints, or support the vehicle.

With the default objective, capture requires `CatchFrame` to stay inside the logical envelope while satisfying total/vertical/horizontal speed, tilt, angular-rate, uprightness, and yaw limits. Yaw tolerance interpolates from 30 deg to 10 deg, capture half-size from 8 m to 3 m, and stable hold from 0.15 s to 0.45 s. The logical platform geometry remains curriculum configuration; success thresholds and the stable-hold rule belong to the training objective.

## Leg-landing behavior

Observations and reward use `FeetFrame` at the generated landing-foot plane and the measured top of the existing landing-pad collider. Four physical legs are active for the whole scenario; the curriculum never switches collision on midway through training. Heading/yaw angle is not a requirement, but body-axis spin is penalized at every altitude so rotation cannot be used as a free drag device.

Every leg-landing episode starts with all engines off. The policy must infer its
landing-burn ignition and control from altitude, velocity, attitude, actuator
timing, and the remaining state observations. Every configured independent engine
channel remains available throughout training and evaluation. An optional
`Separate Continuous Engine Enable` vehicle setting adds one continuous,
hysteretic enable latch and one latch observation per engine channel. It does
not limit how many engines may run. The setting is off by default and changes
the model schema when enabled, so it requires a new policy.

The first external physical impact latches a propulsion safety interlock for
the remainder of the episode. Main engines and RCS are forced off immediately
and cannot be relit by policy or manual-inference commands. Contact with terrain
or any other non-pad collider ends as a fixed-cost missed-pad failure; it does
not create leg support or earn a pad-contact event.

The physical pad has a fixed 25 m half-size. Each generated foot/strut collider
explicitly reports its own pad contacts; the body does not need to touch the
ground. First contact pays no milestone, because touching is not the objective.
The `+2` stable-support event and terminal success require all four feet, main
engines and RCS physically off, no body/strut strike, position within the
current centre-error radius, and uninterrupted compliance for the current
0.4-1.0 s hold. The terminal reward continuously grades both first-contact
smoothness and final center accuracy. A foot-centre-outside-pad flag remains in
telemetry and as an optional failure-rule experiment, but it is disabled by
default and does not invalidate an otherwise centered four-foot landing. The
same hold independently detects calm four-foot support outside the center
radius and ends it as missed-pad, avoiding a motionless wait for the global
timeout. The default excessive-rebound rule ends the episode after rising more than 0.5 m
above first-contact height or losing all foot contact for more than 0.25 s. The
remaining success criteria are editable in the scenario-specific Termination
Criteria section and are serialized with the run.

## Reward timing

Dense landing shaping is a per-second rate multiplied by `Time.fixedDeltaTime`. First-contact/stable milestones and terminal rewards are one-off events. All contributions use `AddReward`, so a terminal signal is added to shaping accumulated since the previous ML-Agents decision instead of erasing it.

Both landing tasks use a gravity/height-derived descent profile, centering,
uprightness, near-target velocity/rotation, and control effort. Chopstick catch
uses signed 3D goal closure. Leg landing instead rewards signed horizontal
closure, so passive falling cannot masquerade as navigation, and adds a global
angular-rate cost plus body-axis spin control. Its Balanced objective has no
per-second time cost, while upward velocity retains its own bounded `0.30/s`
cost. A successful leg touchdown is worth up to `+30`
from contact/center quality, plus up to `+4` success-only mission efficiency at
every difficulty. Mission efficiency combines actual fuel used with a much
larger equivalent-fuel cost for relights than for the first ignition of an
additional engine channel. It rewards economical successful trajectories
without prescribing an engine count or charging failed attempts. The
center-closure reward is stronger, and the bounded progress
potential grades only planar center error. Descent profile, horizontal speed,
tilt, and rotation remain independent shaping channels; lowering altitude alone
therefore cannot manufacture positive navigation progress. Near-pad tilt and
angular-rate costs remain weak aloft. Contact failures
cost `-25` plus up to `-20` impact severity. Severity grows linearly from the
active success boundary to three times that boundary, putting a typical `1.5x`
hard touchdown near `-30` and capping any physical contact failure at `-45`.
Escape and timeout failures cost `-50`; neither early failure nor a repeatable
hard impact is the cheap outcome. Every magnitude and shaping scale remains
editable without code changes; see
[reward_functions.md](../Assets/RocketSim/Scripts/Core/Rewards/reward_functions.md).

## PPO temporal settings

The experiment contract is a 0.01 s physics step and decision period 3: one decision every 0.03 s (about 33.3 Hz).

- `gamma = 0.9995` discounts per policy decision. A reward 10 simulated seconds away retains about 85% of its value. Leg-landing efficiency is terminal and success-gated rather than implemented as an unconditional time cost.
- `lambda = 0.98` is the Generalized Advantage Estimation bias/variance control. Higher values propagate observed outcomes farther back with more variance; lower values rely more on the critic with more bias.
- `time_horizon = 1024` is a rollout chunk, not an episode limit. It covers 30.72 simulated seconds. Longer episodes continue in another chunk and bootstrap the unfinished return from the critic.
- Curiosity is disabled for the primary dense-reward experiment so novelty reward is not an uncontrolled treatment.

## Standard evaluation

Every saved landing policy is evaluated deterministically in a 250-episode stratified suite: 50 paired-seed trials at each of 0%, 25%, 50%, 75%, and 100% curriculum difficulty. Standard evaluation forces clear air, density multiplier 1, no faults, no replay, and the exact scenario-specific training profile generator. It runs at 20x wall-clock time without changing the 0.01 s physics step or decision cadence. Full-rate telemetry, an episode CSV, and aggregate plus per-band JSON statistics are written. See [evaluation_protocol.md](evaluation_protocol.md).

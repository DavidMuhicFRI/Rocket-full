# Landing curriculum experiment

The landing task uses one normalized difficulty value `d` in `[0, 1]`. Spawn
ranges, initial motion, capture tolerances, and the simulated chopstick envelope
are interpolated continuously from the beginner configuration to the configured
full task. There is no easy/hard stage switch and no point at which physical
platform collisions become active.

## Comparison modes

- **Adaptive** is the default. Difficulty advances above 80% recent success,
  retreats below 50%, and holds inside that hysteresis band.
- **Monotonic** uses the same promotion rule but never moves backward.
- **Fixed Full Difficulty** keeps `d = 1` and is the no-curriculum baseline.

Changing the mode in the Run tab resets curriculum counters. This prevents a
new comparison condition from inheriting another condition's learning schedule.
The saved environment configuration records the selected mode and all adaptive
rule parameters.

## Adaptive safeguards

Success is tracked with a cumulative average for the first 32 episodes and an
exponential moving average afterwards. Difficulty updates only after one
parallel-area-sized batch has completed. The default update is limited to 0.02
linear difficulty per batch, backward movement is half as fast as forward
movement, and difficulty cannot retreat more than 0.20 below the highest linear
progress reached.

Fifteen percent of curriculum episodes replay a task profile from 0.20 lower
difficulty. This is a complete per-episode profile, not only an easier spawn: the
agent receives the matching spawn range, success limits, platform size, and
stable-hold time. Replay episodes train PPO but are excluded from the mastery
estimate so easier successes cannot promote the current difficulty. Every
parallel agent freezes its own profile at episode start, so a shared curriculum
update cannot change an episode while it is in flight.

Episode telemetry records global difficulty, the actual per-episode difficulty,
and a replay flag separately. This keeps replay samples identifiable during
analysis while preserving their ordinary success and return measurements.

## Simulated chopstick catch

The landing observation and reward use the booster's `CatchFrame` near the grid
fins and the tower target at the configured catch altitude (60 m by default).
The generated arms and supports are visual only. A trigger-shaped logical volume
is used for geometry, while containment is evaluated directly in code. The
platform never applies a collision impulse, closes arms, creates joints, or
supports the rocket after success.

Capture requires the CatchFrame to remain inside the current volume while the
rocket satisfies the current limits for total, vertical, horizontal, and angular
speed, tilt, and yaw. The required hold time increases continuously with `d`.

## Reward timing

Dense reward terms are treated as rates and multiplied by
`Time.fixedDeltaTime`. Terminal signals use additive reward, so they do not
erase shaping accumulated between ML-Agents decisions. Decision period still
changes how long each selected action is held and must therefore stay fixed
across comparison runs.

The numerical landing reward factors are intentionally unchanged in this pass;
they should be finalized and frozen before producing thesis experiment results.

PPO curiosity is enabled by default with `gamma = 0.99`, `strength = 0.01`, and
`learning_rate = 0.0001`. Because curiosity adds a trainer-side intrinsic reward,
these settings must remain identical across every curriculum comparison. A
curiosity ablation would be a separate experiment, not part of a curriculum run.

# Policy evaluation protocol

Use `Standard Evaluation` inference for thesis measurements. It runs a fixed number of episodes, records full-rate telemetry, writes a JSON aggregate beside the episode CSV, and stops automatically. `Manual Inference` remains for demonstrations and debugging.

## Common evaluator contract

- Episodes per model: 250
- Environment seed: 20257 by default
- Policy: deterministic inference
- Physics step: 0.01 s
- Decision period: 3 physics steps (one action every 0.03 s)
- Wall-clock acceleration: 20x time scale, with the physics and decision intervals unchanged
- Weather: clear, no wind, density multiplier 1
- Faults: disabled
- Telemetry: full-rate step trajectories plus episode summaries

Keep the seed fixed for the primary comparison. Additional seeded suites may be reported as robustness checks only when every condition receives the same suites. Curriculum tasks use 50 episodes at each of `d = 0`, `0.25`, `0.50`, `0.75`, and `1.00`. The same 50 replicate seeds are reused in every band, producing paired initial random draws while the profile itself changes with difficulty.

The saved `TrainingObjectiveConfig` is part of the evaluated treatment. For the selected scenario it contains the reward/cost magnitudes, shaping scales, target geometry, safety rules, and difficulty-dependent success thresholds. Standard Evaluation does not replace it with a reward preset. It adds only the benchmark cycle endpoints described below: a 60-second Fixed Hover horizon and the Hover Track three-capture/per-target-timeout contract. Record the run revision and `currentSessionSha256` from `RunManifest.json` alongside the model checkpoint. When a run has several revisions, inspect that revision's `Session.json` and do not describe the latest configuration as if it governed every checkpoint.

## Landing evaluation

Both landing scenarios are evaluated in five fixed 50-episode difficulty bands with easier-task replay disabled.

- `Chopstick Catch Landing` restores the canonical catch altitude and yaw, then samples the same spawn, platform-size, and capture-criteria profile used by training at each band.
- `Falcon 9 Leg Landing` uses the physical pad/feet task and the same staged spawn, touchdown, stable-contact, and rebound profile used by training at each band.
- Both use their coupled feasibility sampler. Its braking and lateral-control envelope is derived from the selected vehicle's current mass, active engines, gimbal authority, and startup delay.

The evaluator counts success only after the scenario's real terminal criterion: a stable logical catch for chopsticks or stable physical multi-foot contact for legs. Crossing a target altitude is not success.

## Hover evaluation

Fixed Hover does not require an artificial success label. Each standardized episode is a bounded 60-second recovery-and-hold trial, ending earlier only on an enabled safety failure. Reaching the expected horizon records `HoverEvaluationHorizon`, not a training timeout failure or timeout penalty. Analyze time inside a declared hover envelope, position/speed/attitude RMS, fuel use, restart count, and safety terminations. Its JSON summary sets `successMetricDefined: false`, and evaluator progress reports success as `n/a`, rather than presenting a meaningless zero-percent success rate.

Both hover evaluators use the exact training rocket spawn: 50 m altitude, -5 to +5 m on each planar axis, and up to 1 degree tilt per planar axis. Fixed Hover also samples -2 to +2 m/s on each planar velocity axis; Hover Track starts at zero velocity. Both retain the explicit 30 m commanded altitude.

Hover Track uses the same five 50-episode curriculum bands as landing. Target move radius and capture criteria are frozen at the episode's band. An episode succeeds after three captures: the initial offset target plus two relocated targets. Each target must be captured within 35 simulated seconds; otherwise the episode ends with `HoverTrackingTargetTimeout`. Existing ground, attitude, planar, altitude, and fuel safety terminals remain active. The episode CSV and per-band JSON summary report total and relocated capture counts.

The episode CSV already contains mean and standard deviation for enabled step metrics, so RMS can be reconstructed as `sqrt(mean^2 + stddev^2)` for signed error/speed columns. Full-rate evaluation trajectories remain available for time-in-envelope and transient analyses.

## Output artifacts

The episode CSV includes seeds, sampled curriculum difficulty, start conditions, initial mass, minimum commandable nonzero TWR, all-engine minimum-throttle/maximum TWR, termination reason, return, duration, fuel/RCS use, restarts, final position/motion, and mean/standard deviation of every enabled telemetry metric.

When `Reward breakdown` telemetry is enabled, the trajectory schema also includes the raw feature or event count, semantic signed coefficient, and signed contribution for every reward parameter applicable to the selected scenario. The normal reward metrics include separate shaping-rate, one-off event, and terminal totals. This makes a custom objective auditable without reconstructing its contribution signs from source code.

For leg landing it additionally includes:

- whether touchdown occurred and its time;
- feet on pad, outside-pad contact, and structural strike;
- first-contact total/vertical/horizontal speed, tilt, and angular rate;
- stable-hold duration;
- maximum contact impulse and upward rebound height relative to first contact.

The neighboring `*_summary.json` contains completed/successful counts, a `successMetricDefined` flag, a descriptive 95% Wilson interval when success is defined, termination counts, all-episode and successful-only final-state means, resource/control means, and aggregate leg-touchdown statistics. Curriculum tasks additionally contain one analysis block per difficulty band, including band success intervals, endpoint counts, final-state means, and Hover Track capture means. An interrupted evaluator writes `aborted: true`; never combine a partial suite with completed primary runs.

## Training settings held fixed

Within a comparison, hold PPO architecture, the complete selected-scenario objective, curriculum mode, trainer seed policy, total steps, checkpoint schedule, hardware interpretation, and simulation timing constant. This means matching reward magnitudes, shaping parameters, termination switches, and termination thresholds—not merely selecting the same named reward preset. The current declared temporal settings are `gamma = 0.9995`, `lambda = 0.98`, and `time_horizon = 1024`; curiosity is disabled for the primary dense-reward experiments.

Evaluate regular checkpoints to build performance-versus-environment-step curves. Training episodic return is useful for diagnosis but is not a controlled final-performance metric.

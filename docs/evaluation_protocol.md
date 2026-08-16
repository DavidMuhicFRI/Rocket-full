# Policy evaluation protocol

Use `Standard Evaluation` inference for thesis measurements. It runs a fixed number of episodes, records full-rate telemetry, writes a JSON aggregate beside the episode CSV, and stops automatically. `Manual Inference` remains for demonstrations and debugging.

## Common evaluator contract

- Episodes per model: 200 by default
- Environment seed: 20257 by default
- Policy: deterministic inference
- Physics step: 0.01 s
- Decision period: 3 physics steps (one action every 0.03 s)
- Weather: clear, no wind, density multiplier 1
- Faults: disabled
- Telemetry: full-rate step trajectories plus episode summaries

Keep the episode count and seed fixed for the primary comparison. Additional seeded suites may be reported as robustness checks only when every condition receives the same suites.

The saved `TrainingObjectiveConfig` is part of the evaluated treatment. For the selected scenario it contains the complete objective: absolute reward/cost magnitudes, shaping scales and target geometry, plus enabled termination rules and their thresholds. Standard Evaluation fixes the environment and curriculum state described below; it does not silently replace that objective with a preset. Record and compare its `trainingObjectiveSha256` fingerprint and `rewardConfigRevision` from the manifest alongside the model checkpoint. If a run has more than one reward revision, use `RewardConfigHistory.jsonl` to state which checkpoints were trained under each objective; do not describe the latest `RewardConfig.json` as if it governed the entire run.

## Landing evaluation

Both landing scenarios are forced to fixed full difficulty (`d = 1`) with easier-task replay disabled.

- `Chopstick Catch Landing` restores the canonical catch altitude, yaw, and logical platform size, then evaluates the saved objective's full-difficulty capture criteria.
- `Falcon 9 Leg Landing` uses the physical pad/feet task and the saved objective's full-difficulty touchdown, stable-contact, and rebound criteria.
- Both use their coupled feasibility sampler. Its braking and lateral-control envelope is derived from the selected vehicle's current mass, active engines, gimbal authority, and startup delay.

The evaluator counts success only after the scenario's real terminal criterion: a stable logical catch for chopsticks or stable physical multi-foot contact for legs. Crossing a target altitude is not success.

## Hover evaluation

Hover does not require an artificial success terminal. With the default objective, a standardized hover run continues until fuel depletion or another enabled failure terminal, and the evaluator records the trajectory and final state. Fuel depletion, ground impact, unsafe attitude, planar flyaway, altitude flyaway, an optional objective time limit, and external/max-step endings have distinct termination labels. Analyze duration, time inside a declared hover envelope, position/speed/attitude RMS, fuel use, restart count, and the terminal state. Its JSON summary sets `successMetricDefined: false`, and evaluator progress reports success as `n/a`, rather than presenting a meaningless zero-percent success rate.

Standard evaluation restores the catalog hover spawn on every launch (25–35 m altitude, up to 8 m planar offset, -2 to +2 m/s vertical speed, up to 3 m/s horizontal speed, up to 5 deg tilt variation, and up to 8 deg/s angular speed). This deliberately evaluates settled hover near the 30 m target; the 80 m training start remains a recovery-friendly acquisition condition. Manual-inference edits cannot leak into this benchmark. Fixed Hover is selectable directly in Standard Evaluation; Hover Tracking remains manual-inference-only until it has its own frozen evaluator contract.

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

The neighboring `*_summary.json` contains completed/successful counts, a `successMetricDefined` flag, a descriptive 95% Wilson interval when success is defined, termination counts, all-episode and successful-only final-state means, resource/control means, and aggregate leg-touchdown statistics. An interrupted evaluator writes `aborted: true`; never combine a partial suite with completed primary runs.

## Training settings held fixed

Within a comparison, hold PPO architecture, the complete selected-scenario objective, curriculum mode, trainer seed policy, total steps, checkpoint schedule, hardware interpretation, and simulation timing constant. This means matching reward magnitudes, shaping parameters, termination switches, and termination thresholds—not merely selecting the same named reward preset. The current declared temporal settings are `gamma = 0.9995`, `lambda = 0.98`, and `time_horizon = 1024`; curiosity is disabled for the primary dense-reward experiments.

Evaluate regular checkpoints to build performance-versus-environment-step curves. Training episodic return is useful for diagnosis but is not a controlled final-performance metric.

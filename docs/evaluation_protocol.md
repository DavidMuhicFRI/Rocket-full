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

## Landing evaluation

Both landing scenarios are forced to fixed full difficulty (`d = 1`) with easier-task replay disabled.

- `Chopstick Catch Landing` restores the canonical catch altitude, yaw, logical platform size, and full stable-hold rule.
- `Falcon 9 Leg Landing` uses the physical pad/feet task, full touchdown tolerances, and a 1.0 s stable-contact hold.
- Both use their coupled feasibility sampler. Leg-landing ablations use the common single-engine Falcon reference envelope so identical episode seeds produce the same initial task distribution for every hardware preset.

The evaluator counts success only after the scenario's real terminal criterion: a stable logical catch for chopsticks or stable physical multi-foot contact for legs. Crossing a target altitude is not success.

## Hover evaluation

Hover does not require an artificial success terminal. A standardized hover run continues until fuel depletion or an existing failure terminal, and the evaluator records the trajectory and final state. Fuel depletion, ground impact, unsafe attitude, planar flyaway, altitude flyaway, and external/max-step endings have distinct termination labels. Analyze duration, time inside a declared hover envelope, position/speed/attitude RMS, fuel use, restart count, and the terminal state. Its JSON summary sets `successMetricDefined: false`, and evaluator progress reports success as `n/a`, rather than presenting a meaningless zero-percent success rate.

Standard evaluation restores the catalog hover spawn on every launch (25–35 m altitude, up to 8 m planar offset, -2 to +2 m/s vertical speed, up to 3 m/s horizontal speed, up to 5 deg tilt variation, and up to 8 deg/s angular speed). This deliberately evaluates settled hover near the 30 m target; the 80 m training start remains a recovery-friendly acquisition condition. Manual-inference edits cannot leak into this benchmark. Fixed Hover is selectable directly in Standard Evaluation; Hover Tracking, Takeoff, and Belly Flop remain manual-inference-only until each has its own frozen evaluator contract.

The episode CSV already contains mean and standard deviation for enabled step metrics, so RMS can be reconstructed as `sqrt(mean^2 + stddev^2)` for signed error/speed columns. Full-rate evaluation trajectories remain available for time-in-envelope and transient analyses.

## Output artifacts

The episode CSV includes seeds, sampled curriculum difficulty, start conditions, initial mass, minimum commandable nonzero TWR, all-engine minimum-throttle/maximum TWR, termination reason, return, duration, fuel/RCS use, restarts, final position/motion, and mean/standard deviation of every enabled telemetry metric.

For leg landing it additionally includes:

- whether touchdown occurred and its time;
- feet on pad, outside-pad contact, and structural strike;
- first-contact total/vertical/horizontal speed, tilt, and angular rate;
- stable-hold duration;
- maximum contact impulse and rebound height.

The neighboring `*_summary.json` contains completed/successful counts, a `successMetricDefined` flag, a descriptive 95% Wilson interval when success is defined, termination counts, all-episode and successful-only final-state means, resource/control means, and aggregate leg-touchdown statistics. An interrupted evaluator writes `aborted: true`; never combine a partial suite with completed primary runs.

## Training settings held fixed

Within a comparison, hold PPO architecture, reward factors, curriculum mode, trainer seed policy, total steps, checkpoint schedule, hardware interpretation, and simulation timing constant. The current declared temporal settings are `gamma = 0.995`, `lambda = 0.98`, and `time_horizon = 1024`; curiosity is disabled for the primary dense-reward experiments.

Evaluate regular checkpoints to build performance-versus-environment-step curves. Training episodic return is useful for diagnosis but is not a controlled final-performance metric.

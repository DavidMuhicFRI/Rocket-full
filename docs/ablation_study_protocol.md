# Rocket hardware ablation and hover-transfer protocol

## Research questions

The simulator supports two related experiments:

1. How does available control hardware affect PPO learning and final performance on a Falcon 9-like powered leg landing?
2. Does fixed-hover pretraining improve PPO learning on a generic reusable-booster chopstick catch, compared with direct landing training?

The first experiment is a **vehicle hardware ablation**, not a neural-network ablation. Removing engines, grid fins, or RCS changes the physical vehicle, including dry mass and control authority. It does not change the policy architecture: every preset uses the same 109 observations and 39 continuous actions, and unavailable actuator slots are zero observations/no-op actions.

## Part 1: Falcon 9-like leg landing

Select the `Falcon 9 Leg Landing` scenario and one of the named presets below. Each preset starts from the same Falcon 9 timing, 57% minimum throttle, dimensions, propellant model, and actuator parameters.

| UI preset | Engines | Grid fins | RCS | Interpretation |
|---|---:|---|---|---|
| Ablation - Full | 9 independent | On | On | Complete reference configuration |
| Ablation - No Grid Fins | 9 independent | Off | On | Grid-fin hardware removed |
| Ablation - No RCS | 9 independent | On | Off | RCS hardware removed |
| Ablation - Triple Engine | 3 independent | On | On | Three-engine vehicle layout |
| Ablation - Single Engine | 1 independent | On | On | Center-engine vehicle layout |

The triple- and single-engine presets remove engines rather than merely disabling thrust channels. Their lower engine dry mass is therefore part of the treatment. Describe this explicitly in the paper and report initial mass and thrust authority from the run manifest/episode telemetry. The simulator separates minimum commandable nonzero TWR, all-active-engines minimum-throttle TWR, and all-active-engines maximum TWR; do not collapse these into an ambiguous “minimum TWR.” If a later experiment needs “same installed mass, fewer available engines,” add separate availability-only presets; do not mix the two interpretations.

“Nine independent engines” means the policy may command any subset; it is not forced to ignite all nine. Report individual throttle duty so the analysis distinguishes installed authority from the subset the policy actually uses.

All five presets retain the canonical policy schema, so differences cannot be attributed to a different number of neural-network inputs or outputs. They also use the same seeded, single-engine-reference feasibility envelope for initial-state sampling. This is intentional: the initial task distribution stays matched across hardware conditions instead of becoming easier for a weaker vehicle. Preflight still reports each actual vehicle's TWR and reachability diagnostics.

Checkpoints trained before the canonical 109-observation/39-action schema was introduced are not valid sources for this experiment. Start new baseline runs, then transfer only between checkpoints produced by the current schema.

### Physical landing model

`Falcon 9 Leg Landing` is separate from `Chopstick Catch Landing`.

- Four deployed legs are generated at 90-degree spacing with an approximately 18 m footprint. Reference dry mass is assumed to already include the landing gear, so generated geometry adds neither a second leg mass nor an aerodynamic term.
- Each foot and strut has a compound collider that explicitly relays its own pad contacts; the legs deliberately add no aerodynamic drag.
- The existing landing-pad collider supplies the measured contact plane and pad footprint.
- The touchdown reference is `FeetFrame`, not the body origin or grid-fin catch frame.
- Under the default objective, a successful landing requires at least three feet on the pad, no foot outside the pad, no body/strut strike, and all full-difficulty motion/position limits held for 1.0 simulated second.
- First foot contact is evaluated immediately. Excess total, vertical, or horizontal speed, tilt, or angular rate is a hard-touchdown failure.
- The default excessive-rebound rule triggers after rising more than 0.5 m above first-contact height or losing all foot contact for more than 0.25 s. The body is not expected to contact the ground.
- A body/strut strike, foot outside the pad, excessive rebound, missed pad, unsafe attitude, fuel depletion while airborne, flyaway, or time limit is a distinct terminal reason.
- Physics solver iterations are raised only during this contact-heavy scenario and restored afterwards. The landing pad remains kinematic/static; there are no closing arms or joints.

This is a best-effort rigid-body stability model, not a structural model of Falcon 9 landing-leg deployment or failure. Do not interpret contact impulse as a certified leg load.

These contact counts, holds, limits, rule switches, and linked terminal costs are scenario-specific objective parameters. Freeze them before the ablation; changing one creates a different experimental treatment.

### Continuous curriculum

Leg landing has independent curriculum progress, its own faster terminal-descent spawn curve, and the same declared success-tolerance/adaptive rules as chopstick landing. Heading angle is irrelevant, while body-axis spin is penalized globally; the pad half-size stays 10 m, and the default stable-contact hold grows from 0.25 s to 1.0 s. Every episode starts with all engines off, so ignition timing and engine selection remain part of the learned task.

Use one curriculum mode consistently within an experiment:

- `Adaptive`: promote above 80% recent success, retreat below 50%, and limit retreat to 0.20 below peak linear progress.
- `Monotonic`: the same forward rule without retreat.
- `Fixed Full Difficulty`: `d = 1`, used for the no-curriculum baseline and all standard evaluations.

Fifteen percent of training episodes replay the complete task profile at `max(0, d - 0.20)`. These episodes still train PPO but do not update the mastery estimate.

## Part 2: hover pretraining to chopstick catch

For every hardware configuration included in this experiment, train a matched pair with the identical preset and policy schema:

| Condition | Initialization | Chopstick training budget |
|---|---|---:|
| Direct | New random PPO weights | L steps |
| Hover transfer | New run initialized from that configuration's fixed-hover checkpoint | L steps |

Use ML-Agents `--initialize-from` for transfer. Do not use `--resume`, because resume continues the old run identity, step counter, and optimizer state as one training run. The common 109/39 schema now makes hover-to-landing transfer possible even when hardware is absent, but the source and target hardware preset should still match so the comparison measures task transfer rather than simultaneous hardware adaptation.

Report both landing-only sample cost (`L`) and total interaction cost (`H + L`) for the transferred condition. If compute permits, add a direct budget-matched control trained on chopstick landing for `H + L` steps.

Hover does not need an artificial “landed” terminal event. Its evaluator can run the existing episode until fuel depletion or another terminal condition and record the entire trajectory plus the terminal state. For selecting a pretraining checkpoint, prefer trajectory measures such as time in the hover envelope, position/speed RMS, survival duration, fuel use per second, and restart count; a single fuel-out state is not sufficient by itself.

## Controlled experiment contract

- Use at least five independent trainer seeds per condition; three is a pilot-study minimum.
- Reuse the same trainer-seed list and evaluation episode seeds across conditions.
- Use deterministic inference and 200 episodes per saved landing policy in the primary evaluation.
- Keep `Time.fixedDeltaTime = 0.01 s`, decision period `3`, PPO hyperparameters, the complete selected-scenario objective, curriculum rule, training budget, checkpoint schedule, weather, and faults fixed across a comparison. The objective includes reward magnitudes, shaping geometry, termination switches, and termination thresholds; a matching reward-preset label alone is not sufficient.
- Use clear weather/no faults for the primary evaluation. A seeded-wind suite may be a separately named robustness experiment.
- Keep PPO curiosity disabled for the primary dense-reward experiment. Enabling it is a separate treatment.
- Treat the independently trained policy seed, not each evaluation episode, as the replicate for claims about learning.
- Evaluate regular checkpoints. Training return is a diagnostic and is not a substitute for fixed-suite evaluation success.

## Metrics

### Primary landing metrics

1. Standard-evaluation success rate per training seed, with pooled Wilson intervals used only as descriptive episode-level uncertainty.
2. Area under evaluation success versus environment steps.
3. Steps to a predeclared success threshold (for example 80%); runs that never reach it remain censored/non-reaching.
4. Fuel used per successful landing.

### Touchdown quality

Report distributions and separate successful-only values from all-episode terminal values:

- first-contact total, vertical, and horizontal speed;
- first-contact tilt and angular rate;
- final planar error;
- feet on pad and stable-hold duration;
- maximum contact impulse and maximum rebound height;
- structural-strike and foot-outside-pad rates;
- time to first contact and time to stable touchdown;
- termination-reason proportions.

Contact impulse is a simulator diagnostic, not a real structural certification metric.

### Hover quality

- episode/survival duration and terminal reason;
- altitude-error and planar-error RMS over the evaluation trajectory;
- vertical-speed, horizontal-speed, tilt, and angular-rate RMS;
- fraction of simulated time inside the predeclared hover envelope;
- peak altitude overshoot/undershoot;
- fuel used per simulated second and engine restart count;
- final position, velocity, tilt, and angular rate at fuel depletion or other termination.

### Control and hardware interpretation

- individual engine throttle and gimbal duty;
- throttle, gimbal, and fin saturation fractions;
- engine restart count;
- RCS active fraction and propellant used;
- fin effort;
- installed/active engines, initial mass, minimum commandable nonzero TWR, all-engine minimum-throttle/maximum TWR, observation count, and action count;
- wall-clock training time and simulation throughput.

These measurements help distinguish missing physical authority from failure to learn to use available authority.

## Statistical reporting

- Plot median and interquartile range across trainer seeds for learning curves.
- For final comparisons, show every trained seed and a 95% bootstrap confidence interval across seeds.
- Use paired comparisons because conditions share trainer seeds and evaluation episode seeds.
- Predeclare primary metrics/comparisons before the full run set.
- Do not treat hundreds of episodes from a few policies as hundreds of independently trained policies.
- Do not compare raw return across different tasks or reward definitions.

## Saved evidence

Each run records configuration snapshots, a run manifest, episode summaries, and sampled/full-rate trajectories. The manifest includes scenario, hardware flags, installed/active engines, initial mass and the three thrust-authority values, policy dimensions, seeds, timing, transfer source, Unity/ML-Agents/Python/PyTorch/CUDA versions, trainer GPU/host details, Git state, `trainingObjectiveSha256` for the serialized four-scenario objective, and fingerprints for the complete hardware and generated ML-Agents configurations. Leg-landing telemetry adds per-foot contact, first-contact motion, stable hold, contact impulse, rebound, body strike, and outside-pad fields. Optional reward-breakdown telemetry adds each applicable parameter's raw feature, signed coefficient, and signed contribution. Standard evaluation writes a neighboring JSON aggregate with success intervals, outcome counts, final-state statistics, fuel/control use, and leg touchdown statistics.

## Final-run readiness gate

Pilot runs may be used to choose a complete objective and debug the protocol. Before collecting thesis data:

- freeze the selected scenario's reward magnitudes, shaping parameters, termination rules, and thresholds, and write them into the preregistered experiment table;
- commit or tag the complete simulator state so `sourceControlDirty` is false;
- use a unique run ID for every seed and condition; the launcher refuses to overwrite an existing results directory unless Resume is explicitly enabled, and Resume accepts only the current saved environment/curriculum state with identical scenario, objective, hardware, and generated ML-Agents configuration;
- verify that every run manifest reports a successful Python-environment probe and the expected package/GPU versions;
- use the same root commit, `trainingObjectiveSha256`, PPO YAML contract, and evaluator seed across every condition in the comparison.

The current run manifest is a clean objective-schema boundary. Runs created by earlier schemas are not resume, inference, or transfer sources for this protocol; train fresh sources and targets under the current four-scenario configuration.

`TrainingConfig.yaml` at the project root is a synchronized manual-CLI reference. Normal UI launches generate and consume the run-local copy saved beside each experiment's other configuration files.

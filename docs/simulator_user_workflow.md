# RocketSim user workflow

RocketSim is a configurable reinforcement-learning sandbox for reusable-booster
control. Vehicle, task, reward, environment, trainer, fault, and telemetry
settings are independent configuration areas. Ordinary edits and vehicle/task
presets do not silently replace another area; explicitly loading a saved run
restores the run-owned configuration described below.

## Configure a vehicle

The Vehicle tab offers two immutable starting points:

- **Falcon 9**: Falcon 9-like body and engine parameters, an octaweb with the
  center-plus-two group active, four grid fins, and RCS.
- **Simple**: one engine, no fins or RCS, and training-friendly actuator timing.

Every field can be edited after selecting a built-in. The result becomes a
custom vehicle. Enter a name under **Saved Vehicle Presets** and use
**Save / Overwrite** to create or replace a user preset. Selecting a saved row
loads it; **Delete Selected** removes it. Built-ins cannot be overwritten or
deleted. The preset catalog contains only `RocketPartsConfig`, so loading a
vehicle never changes rewards, task settings, weather, trainer parameters, or
telemetry.

Task changes preserve engine layout, burn group, independent-control selection,
fins, and RCS. When **Use Task Recommended Fuel** is enabled, only starting fuel
is updated for the selected task.

## Policy interface

RocketSim derives ML-Agents vector sizes from the vehicle before agents start.
General flight state and the four landing-foot contact values are always
observed. Each commandable engine channel contributes throttle, two gimbal
values, four engine-state flags, and constraint time. Enabled fins and RCS jets
contribute one state observation and one action each. Disabled hardware does not
create padded observations or no-op actions. Network width and depth remain the
trainer settings chosen in the ML tab.

Because policy sizes depend on hardware, a model or initialization checkpoint
must use matching engine, fin, and RCS channels. Saved run manifests record the
resolved observation and action sizes.

## Train and evaluate

1. Choose or build a vehicle.
2. Select a task. Full-width task rows show the complete descriptions.
3. Configure the environment and run identity.
4. Configure rewards and termination criteria.
5. Review ML-Agents and telemetry settings.
6. Use **Check Configuration**, then start training.
7. Export the trained ONNX model to the existing RocketSim model resource path.
8. Load the saved run in Inference and run Standard Evaluation.

The landing sampler clips randomized starts using the selected vehicle's mass,
active thrust, gimbal authority, and startup delay. Preflight reports the same
vehicle-specific reachability and thrust-authority diagnostics.

### Run-owned reward setup

Starting training writes the complete `TrainingObjectiveConfig` for all tasks to
the selected run directory. `RewardConfig.json` is the latest reward, shaping,
and termination setup and `trainingObjectiveSha256` identifies it in
`RunManifest.json`. The same objective is also embedded in the environment
snapshot so a run remains self-contained.

Enabling **Resume Run** restores the saved run configuration, including rewards,
and rebuilds the Reward tab from it. Selecting **Initialize From** keeps the new
run's task, vehicle, environment, and trainer configuration but loads the source
run's complete reward setup before training. The user can then edit it normally.

Starting or resuming training always overwrites `RewardConfig.json` with the
values visible in the Reward tab. Every launch is also appended to
`RewardConfigHistory.jsonl`; reward changes advance `rewardConfigRevision` in the
manifest. This makes deliberate mid-run reward changes possible without erasing
the objective used by earlier checkpoints. For controlled experiments, prefer a
new Run ID when changing rewards unless the change itself is part of the method.

## Suggested diploma demonstrations

- **Simple hover** demonstrates the complete configuration, training, telemetry,
  and inference workflow with a small control interface.
- **Falcon 9 leg landing** demonstrates curriculum learning, physical landing
  contacts, a higher-dimensional vehicle, and deterministic evaluation.

Compare physical task metrics rather than raw return when reward definitions
differ. Hover reporting should emphasize altitude/speed/tilt stability, fuel,
engine restarts, and duration. Leg-landing reporting should emphasize success
rate, touchdown position and motion, attitude, stable contacts, rebound/body
strikes, fuel, and restarts.

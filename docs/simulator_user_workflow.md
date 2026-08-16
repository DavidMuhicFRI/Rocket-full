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

### Run-owned simulation session

Starting training freezes the complete configuration visible in the panel: the
vehicle, task and curriculum, environment and faults, reward objective, ML
settings, and telemetry settings. The snapshot is stored under the run ID as an
immutable numbered revision such as `revisions/0001/Session.json`.

`RunManifest.json` points to the latest revision and records its SHA-256 hash,
resolved observation/action sizes, hardware channels, and network shape. Live
curriculum counters are stored separately in `RuntimeState.json`; updating
progress therefore cannot rewrite the configuration that launched a checkpoint.

Enabling **Resume Run** restores the saved session, including rewards, and
rebuilds the panel from it. Selecting **Initialize From** keeps the new run's
configuration but loads a compatible source checkpoint. The user can edit the
draft before launching.

Starting or resuming creates a new immutable session revision. Earlier revisions
remain available, so a reward or environment change never erases what an older
checkpoint used. Resume is allowed only while the policy interface and network
architecture remain checkpoint-compatible. For controlled experiments, prefer a
new Run ID when changing the treatment unless continuation is itself part of the
method.

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

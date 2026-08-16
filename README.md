# Reusable Booster Simulator

A Unity and ML-Agents sandbox for configuring, training, evaluating, and
inspecting reusable-booster control policies. The simulator supports hover,
moving-target hover, Falcon 9-style leg landing, and kinematic chopstick catch
tasks. Vehicle hardware, task conditions, rewards, learning settings, faults,
and telemetry are user configurable.

## Requirements

- Unity `6000.4.1f1`
- Unity ML-Agents package `4.0.2`
- The existing Python/Conda ML-Agents environment configured in the Run tab

Open `Assets/Scenes/ConfigScene.unity` and use the right configuration
panel to prepare a vehicle and task. Run **Check Configuration** before starting
training or inference. A launch freezes the complete panel state into a session
revision under `results/<run-id>/`; the active simulation never reads later
panel edits.

The two built-in vehicle presets are immutable starting points:

- **Simple**: one engine with no fins or RCS;
- **Falcon 9**: Falcon 9-like body, octaweb, grid fins, and RCS.

User presets contain vehicle settings only and can be named, overwritten, or
deleted from the Vehicle tab. Policy observation and action sizes are derived
from the selected task and enabled hardware, so models are compatible only with
the schema recorded by their run.

## Documentation

- [User workflow](docs/simulator_user_workflow.md)
- [Runtime ownership and flow](docs/runtime_flow.md)
- [Simulation-session lifecycle](docs/simulation_session_lifecycle.md)
- [Reward definitions](docs/reward_functions.md)
- [Landing curriculum](docs/landing_curriculum.md)
- [Evaluation protocol](docs/evaluation_protocol.md)
- [Architecture overview](docs/rocket_sim_architecture_overview.svg)
- [Detailed architecture graph](docs/rocket_sim_architecture_visual.svg)

Runtime code is compiled in `RocketSim.Runtime`; editor regression tests are
isolated in `RocketSim.EditorTests`. Old run schemas are intentionally not
migrated because the current project defines a clean product format.

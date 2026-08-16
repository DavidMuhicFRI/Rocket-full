# Refactor behavior contract

This document records the simulator behavior that the architecture refactor must preserve unless a later phase explicitly replaces it with a documented design.

## Experiment configuration

- Vehicle controls determine the policy action and observation sizes. Disabled hardware does not create padded policy channels.
- The built-in Falcon 9 and Simple presets change vehicle configuration only.
- User vehicle presets can be created, overwritten, and deleted without changing task, reward, environment, learning, or telemetry settings.
- Selecting a task does not silently replace the selected engine layout or burn group.
- Training uses fixed physics steps and applies rate rewards once per simulated second by multiplying them by `Time.fixedDeltaTime`.

## Episode behavior

- Hover starts with its active engines running at the calculated equilibrium throttle. This is an initial physical condition, not a reward target.
- Landing starts with engines off and samples coupled altitude and velocity values from a conservative recoverability envelope.
- Landing flyaway altitude is measured relative to the episode start altitude.
- Leg landing uses physical foot contacts and structural-contact markers. Chopstick landing uses the configured kinematic capture platform.
- Engine timing, minimum throttle, restart cooldown, RCS minimum pulse duration, wind, and configured faults remain observable through their physical effects and relevant policy state.

## Reproducibility

- Episode, wind, curriculum replay, target, and fault sampling use deterministic streams derived from the configured seed, area index, and episode index.
- Training configuration, reward configuration, vehicle configuration, generated trainer YAML, policy dimensions, environment provenance, and telemetry configuration are recorded before a trainer starts.
- Resume and policy initialization reject incompatible policy interfaces.
- Evaluation uses deterministic seeds and records terminal outcome metrics independently from training telemetry.

## Regression gates

Every refactor phase must satisfy these checks before it is committed:

1. `dotnet build Falcon9.sln --no-restore` completes without compiler errors.
2. The Unity editor reports no new script-compilation errors.
3. Existing reward, actuator, landing, curriculum, run-contract, policy-schema, evaluation, and preset tests remain valid or are replaced by equivalent tests for the new ownership model.
4. Prefab references required by the runtime are validated after component ownership changes.
5. No active runtime receives the editable configuration object used by the right configuration panel.

## Intentional clean break

The refactor defines a new run/session schema. Runs written by earlier schemas are not migrated or guessed at. They are ignored by run discovery and rejected with a clear unsupported-schema message when addressed directly.

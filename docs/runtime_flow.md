# Runtime flow and responsibilities

The simulator uses one-way configuration flow. Runtime systems never edit the
right panel's draft.

```text
RightConfigPanel
  -> SimulationSessionDraft
  -> SimulationRunCoordinator
  -> immutable SimulationSessionSnapshot
  -> SimulationAreaHost runtime copy
  -> RocketAssembly and FalconAgent instances
```

## Ownership

- `RightConfigPanel` builds controls and edits the session draft. It does not
  read or write run files.
- `SimulationSessionLoader` reads and validates saved sessions for preview.
- `SimulationRunService` is the application boundary for run discovery,
  compatibility checks, immutable revision storage, and runtime-state storage.
- `SimulationRunCoordinator` validates and freezes a draft, then chooses the
  training or inference path.
- `TrainingRunController` owns the Python process and communicator-port
  handshake. It does not persist sessions or build UI.
- `EvaluationRunController` owns evaluation outcome collection, summary output,
  and safe end-of-frame completion.
- `SimulationAreaHost` creates and removes simulation areas from a private
  runtime copy of the snapshot.
- `VehiclePreviewController` creates and freezes only the dummy vehicle shown
  while configuration is editable.

## Vehicle hardware

`RocketAssembly` is the one adapter between the saved vehicle configuration and
the scene vehicle. It forwards body, engine, fin, RCS, and landing-gear settings
to their matching component. Components own their geometry and presentation;
the agent reads their resulting physical state but does not construct hardware.

The landing-gear component is serialized on the vehicle prefab and enabled only
for leg landing. Its simple struts and feet are resized from the configured body
dimensions when the vehicle is prepared. They provide contact and stability only:
landing-leg drag, deployment dynamics, and structural deformation are outside
the simulator model.

The policy interface is derived from the selected session. Disabled engines,
fins, and RCS channels are absent, and the four foot-contact observations exist
only for leg landing. Consequently, a model may be resumed or loaded only when
both the vehicle controls and task-specific observation count are compatible.

## Environment, faults, and actuators

`WindEnvironmentRuntime` owns prevailing wind and gust state for one area. The
agent reads its current vector when calculating relative airspeed and telemetry.
`EpisodeFaultRuntime` independently selects seeded training faults or activates
the configured evaluation fault, then exposes only the resulting severity for a
specific actuator. This keeps wind and fault selection out of agent physics.

Engine and RCS timing rules live in `EngineActuatorStateMachine` and
`RcsActuatorStateMachine`. `FalconAgent.Actuators` translates policy commands,
advances those state machines, and applies their final state to the vehicle.
RCS indexing is defined once in `RcsHardwareLayout`; the component owns pod and
force-point lookup, while presentation helpers own generated geometry and plumes.

## Training

The coordinator validates the complete session, checks checkpoint compatibility,
saves the immutable run revision, gives a runtime copy to the area host, starts
telemetry, and delegates Python startup to `TrainingRunController`. Agents are
spawned only after the trainer port is ready.

## Inference and evaluation

Inference does not start Python. The coordinator creates an in-memory snapshot,
installs its runtime copy, starts evaluation telemetry when requested, and asks
the area host for one model-driven area. Standard evaluation applies its frozen
evaluation contract only to that runtime copy, not to the editable or persisted
training session.

## Stop and failure

A failure before spawning drops the prepared runtime and returns the preview to
the draft. Stop terminates the Python process tree when present, ends evaluation
subscriptions, removes active areas, disables logging, and rebuilds the preview.

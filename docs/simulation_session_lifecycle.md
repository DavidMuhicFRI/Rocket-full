# Simulation session lifecycle

A simulation session is the single configuration passed from the right panel to
training, inference, evaluation, or preview. It contains five independent
sections:

- `vehicle`: body, fuel, engines, fins, RCS, and landing-leg configuration;
- `environment`: selected task, curriculum, wind, atmosphere, and faults;
- `objective`: reward terms, target geometry, and termination rules;
- `learning`: ML-Agents trainer and network settings;
- `telemetry`: recorded groups, sampling, and output settings.

Run identity and live learning progress are deliberately not part of this value.
The run ID selects storage. `RuntimeState.json` stores mutable curriculum
counters separately.

## Draft, snapshot, and runtime copy

1. The panel edits one `SimulationSessionDraft`.
2. Loading a run replaces the draft but retains the pre-load draft until the
   user accepts the import or restores it.
3. Starting a run validates the whole draft and creates a deep-copied
   `SimulationSessionSnapshot`.
4. The snapshot is saved before launch and the runtime receives another deep
   copy. Later panel edits cannot mutate a running experiment.

## On-disk layout

```text
results/<run-id>/
  RunManifest.json
  RuntimeState.json
  TrainingConfig.yaml
  revisions/
    0001/
      Session.json
      TrainingConfig.yaml
      LaunchManifest.json
    0002/
      ...
```

Revision directories are immutable. `RunManifest.json` is only an index to the
latest revision and contains the resolved policy dimensions needed for model
compatibility checks. `TrainingConfig.yaml` at the run root is a generated copy
for the ML-Agents command line; the authoritative YAML is stored with its
revision.

## Resume rules

A resumed checkpoint may use changed rewards, environment values, physical
parameters, or optimizer values. It cannot change the observation/action
interface, trainer type, behavior name, normalization setting, hidden width, or
network depth. Such a change needs a new run because the saved neural-network
weights are structurally incompatible.

Old run layouts are intentionally not migrated. Run discovery accepts only the
current session schema and a matching snapshot hash.

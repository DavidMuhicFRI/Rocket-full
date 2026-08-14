# Reward functions

The detailed Unity-visible reward narrative is currently stored at:

`../Assets/RocketSim/Scripts/Core/Rewards/reward_functions.md`

The executable source of truth is:

- `../Assets/RocketSim/Scripts/Core/Rewards/TrainingObjectiveConfig.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/RewardPresetCatalog.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/RewardParameterCatalog.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/ShapingParameterCatalog.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/TerminationRuleCatalog.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/ObjectiveValidator.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/RewardContributionBuffer.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/RocketRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/ChopstickLandingRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/LegLandingRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/HoverRewardModel.cs`

The guide distinguishes the logical chopstick catch from physical leg landing and
documents the four supported scenarios: chopstick landing, leg landing, fixed hover,
and moving-target hover. It also explains absolute reward magnitudes, shaping
geometry, scenario-aware termination criteria, validation, and contribution
telemetry. Treat executable code and regression tests as authoritative if an
objective is changed during experiment development.

## Objective editor contract

The Rewards tab edits one independent `ScenarioObjectiveConfig` for the selected
task. It shows every applicable reward parameter, including zero-valued signals,
and omits criteria that cannot apply to that scenario. There are exactly four
task objectives: Chopstick Catch Landing, Falcon 9 Leg Landing, Hover, and Hover
Track.

- Reward rows are actual magnitudes, never multipliers. Green rows add their
  value; red rows store a nonnegative cost magnitude that the evaluator negates.
- Continuous rows are rates integrated by `Time.fixedDeltaTime`; event and
  terminal rows are applied once. Setting a magnitude to zero disables that
  signal without removing its formula or terminal rule.
- Every slider has a synchronized keyboard field for precise values and
  scientific notation. Reward sliders use a zero-inclusive quadratic curve so
  small values remain adjustable without pushing normal preset values against
  the right edge; logarithmic threshold sliders include an exact-zero detent;
  typed expert values may use the catalog's wider validated hard range.
- A reward preset replaces the scenario's complete reward vector. It does not
  change shaping geometry or termination rules. Row Reset returns to the last
  selected preset, while Reset full objective restores Balanced rewards and the
  default shaping and termination configuration.
- Advanced shaping controls edit feature falloffs, normalizers, and target
  curves separately from reward magnitude.
- Termination cards expose scenario-specific success, failure, and limit rules.
  A disabled rule keeps its threshold values but does not end the episode or
  apply its linked terminal outcome. Curriculum thresholds expose independent
  Initial and Full endpoints. At least one rule must remain enabled because the
  agent deliberately has no hidden `MaxStep` episode limit.

`ObjectiveValidator` reports invalid ranges and inconsistent configuration in
the editor and before launch. Errors block startup; warnings—including an
intentional all-zero reward vector—remain visible without silently rewriting
the user's objective.

The landing curriculum design and comparison modes are documented in
[`landing_curriculum.md`](landing_curriculum.md).

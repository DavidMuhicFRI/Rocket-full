# Training objective runtime

The simulator supports four training tasks:

- chopstick landing
- physical leg landing
- fixed hover
- moving-target hover

## Single source of truth

Each task owns one `ScenarioObjectiveConfig` with three independent groups:

1. `RewardParameters` contains absolute, nonnegative magnitudes.
2. `RewardShapingParameters` contains feature scales and target-curve geometry.
3. `TerminationParameters` contains rule switches and thresholds.

There are no hidden reward multipliers. Applying a preset writes a complete
vector of actual magnitudes. A cost is stored as a positive magnitude and the
runtime applies its negative sign. Zero disables a signal without changing its
formula.

The evaluator does not clamp or repair a configuration. Validation happens when
training starts, and the resulting objective remains fixed for the run.

## Reward cadence

Continuous terms are reward **rates**. `FalconAgent` integrates the returned
`RewardDecision.shapingRate` using the physics interval:

```text
step shaping reward = shaping rate * Time.fixedDeltaTime
```

Event and terminal values are applied once and are not multiplied by time. This
keeps the objective consistent when the physics timestep changes.

The implementation follows one direction in `FalconAgent.Rewards`: measure the
state, build a measurement-only `RewardRuntimeContext`, evaluate the selected
task model, apply the three reward cadences, and finally end the episode if the
decision is terminal. Task models never mutate the agent or configuration.

## Diagnostics

Pass a reusable `RewardContributionBuffer` to `RocketRewardModel.Evaluate` to
record each applicable `RewardParameterId` without per-step allocation. It
snapshots even dormant event and terminal coefficients, and exposes:

- the raw feature or event count;
- the semantic signed coefficient (`+magnitude` for rewards, `-magnitude` for costs);
- their signed product;
- totals for shaping rate, event reward, and terminal reward.

The buffer is cleared automatically at the start of each evaluation. A caller
should allocate one buffer per agent, never one per physics step.

## Shared notation

For an error magnitude `x` and configurable scale `s`:

```text
Exp01(x, s) = exp(-abs(x) / abs(s))
```

Only a small numerical epsilon protects division by zero. It does not represent
a task threshold or hidden shaping choice.

`d` denotes the episode's frozen curriculum difficulty in `[0, 1]`. A
`DifficultyRange(initial, full)` is evaluated as:

```text
value(d) = lerp(initial, full, clamp01(d))
```

## Landing guidance

Both landing tasks share the same suicide-burn-oriented descent features. Let:

```text
h               = max(0, altitude - terminalAltitude)
v_descent       = sqrt(2 * gravity * h) * landingBallisticDescentFraction
v_vertical_goal = -v_descent
descent_error   = clamp01(abs(verticalSpeed - v_vertical_goal)
                          / max(landingDescentErrorMinimumScaleMps, v_descent))
signed_closure  = clamp(goalClosureRate
                        / max(landingClosureMinimumScaleMps, v_descent), -1, 1)
planar_error    = 1 - Exp01(planarDistance, landingPlanarDistanceFalloffM)
upright_error   = 1 - upright01
near_target     = Exp01(h, landingNearTargetAltitudeFalloffM)
planar_speed    = clamp01(planarSpeed / landingPlanarSpeedScaleMps)
angular_rate    = clamp01(angularRateDegS / landingAngularRateScaleDegS)
```

The common rate is:

```text
+ landingGoalClosureRewardRate             * signed_closure
- landingDescentProfileErrorCostRate        * descent_error
- landingPlanarDistanceCostRate             * planar_error
- landingUprightErrorCostRate               * upright_error
- landingNearTargetPlanarSpeedCostRate      * near_target * planar_speed
- landingNearTargetAngularRateCostRate      * near_target * angular_rate
- controlEffortCostRate                     * clamp01(controlEffort)
- timeCostRate
```

The desired vertical speed naturally approaches zero at ground level. The model
does not reward a prescribed throttle, engine state, or ignition time; PPO must
discover when thrust is necessary to follow that profile.

### Chopstick-specific shaping

```text
yaw_error = clamp01(abs(yawErrorDeg) / landingYawErrorScaleDeg)

- landingYawErrorCostRate * near_target * yaw_error
```

`stableCaptureReward` is emitted once when the logical capture first becomes
stable.

### Leg-specific shaping

```text
yaw_spin = clamp01(abs(yawRateDegS) / landingYawSpinScaleDegS)

- landingYawSpinCostRate * yaw_spin
```

This independent body-axis term prevents the policy from using fins to spin
around the rocket's vertical axis while still appearing upright. The following
events are one-offs:

- `firstFootContactReward`
- `stableTouchdownReward`

## Hover baseline

Fixed hover and moving-target hover share:

```text
+ hoverAltitudeProximityRewardRate * Exp01(verticalError, hoverAltitudeFalloffM)
+ hoverPlanarProximityRewardRate   * Exp01(planarDistance, hoverPlanarFalloffM)
+ hoverUprightRewardRate           * upright01
+ hoverSpeedCalmRewardRate         * Exp01(speed, hoverSpeedFalloffMps)
+ hoverRotationCalmRewardRate      * Exp01(angularRateDegS,
                                           hoverAngularRateFalloffDegS)
- hoverLinearSpeedCostRate         * speed
- hoverAngularRateCostRate         * angularRateDegS
- controlEffortCostRate            * controlEffort
```

Each ignition after a previous shutdown emits:

```text
- engineRestartCost * restartCountThisStep
```

Initial scenario ignition is not a restart and must not be reported as one by
the agent.

## Moving-target hover

The effective capture radius is
`trackingCaptureRadiusM.At(curriculumDifficulty01)`. Inside it, the settle terms
are:

```text
tight_center     = Exp01(planarDistance,
                         max(trackingTightCenterMinimumFalloffM,
                             captureRadius * trackingTightCenterRadiusFraction))
horizontal_calm = Exp01(planarSpeed, trackingHorizontalCalmFalloffMps)
vertical_calm   = Exp01(verticalSpeed, trackingVerticalCalmFalloffMps)
rotation_calm   = Exp01(angularRateDegS, trackingRotationCalmFalloffDegS)

+ trackingSettleCenterRewardRate         * tight_center
+ trackingSettleHorizontalCalmRewardRate * horizontal_calm
+ trackingSettleVerticalCalmRewardRate   * vertical_calm
+ trackingSettleRotationCalmRewardRate   * rotation_calm
+ trackingSettleCompositeRewardRate      * tight_center * horizontal_calm
                                          * vertical_calm * upright01
- trackingSettlePlanarSpeedCostRate      * planarSpeed
- trackingSettleVerticalSpeedCostRate    * abs(verticalSpeed)
```

Outside the capture radius, the approach terms reward useful motion toward the
target and cost retreat, loitering, and overspeed. Every normalization and
threshold used by these terms is represented in `RewardShapingParameters`.

A completed capture emits `trackingTargetCaptureReward`. If
`trackingCaptureGoalEnabled` is on and the capture count reaches
`trackingRequiredCaptures`, the same step ends successfully with
`trackingCaptureGoalReward` and reason `HoverTrackingCaptureGoal`.

## Termination behavior

Rule switches are checked before their thresholds. Disabling a rule means it
does not end the episode and its terminal reward/cost is not applied. Simulator
integrity failures and ML-Agents external resets remain outside this configurable
policy. Validation requires at least one enabled rule because the agent's
`MaxStep` is intentionally zero; this keeps every episode boundary explicit in
the selected objective.

Landing stability criteria also define the `stableCaptureReward` and
`stableTouchdownReward` events while those magnitudes are nonzero. Moving-target
capture criteria always define target-capture events, target movement, and
curriculum accounting even when capture-count termination is disabled. Their
thresholds therefore remain active and validated for those non-terminal roles.

### Chopstick priority

1. unsafe attitude
2. horizontal flyaway
3. fuel depletion
4. altitude escape above the episode start
5. capture-plane crossing (success or failed capture)
6. time limit

Capture success requires all curriculum-interpolated position, speed, tilt,
angular-rate, and yaw limits. When `chopstickRequireStablePlatform` is enabled,
the capture envelope and stable-hold requirement must also be satisfied.

### Leg-landing priority

1. structural strike
2. foot outside the pad
3. hard first contact
4. excessive rebound or sustained loss of all foot contacts
5. stable touchdown success
6. unsafe attitude
7. horizontal flyaway
8. fuel depletion
9. altitude escape above the episode start
10. missed pad
11. time limit

Hard first contact and stable touchdown use the interpolated landing envelope.
Stable touchdown additionally requires the configured minimum number of feet and
hold duration. Rebound measurements are sticky after first contact and use
`legMaximumReboundRiseM` and `legMaximumAllFeetContactLossSeconds`.

When `legAllowFuelDepletionAfterContact` is enabled, empty fuel does not end the
settling hold after contact has begun.

### Hover priority

1. ground-clearance crossing
2. unsafe attitude
3. fuel depletion
4. horizontal flyaway
5. absolute altitude ceiling
6. moving-target capture goal, when applicable
7. optional time limit

A capture goal deliberately wins over a simultaneous time-limit boundary, but
never over a simultaneous safety failure.

## Required agent update order

For moving-target hover, event ordering is part of correctness:

1. update capture readiness using the objective's interpolated capture limits;
2. set `hoverTrackTargetCapturedThisStep` and increment the episode count;
3. build `RewardTerms` and `RewardRuntimeContext` for the current target;
4. evaluate and apply shaping, event, and terminal values;
5. notify the shared capture counter; only if the episode continues, randomize
   the target and reset the capture hold timer.

Randomizing before evaluation would calculate the capture reward against the
next target. Evaluating before the capture update would delay or lose the event.

Chopstick stable time, leg stable time, and rebound state must likewise be
updated before reward evaluation using the current objective's effective
termination thresholds.

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

Both landing tasks share the gravity/height descent guide, position, attitude,
near-target velocity, and rotation features. Let:

```text
h               = max(0, altitude - terminalAltitude)
v_descent       = sqrt(2 * gravity * h) * landingBallisticDescentFraction
v_vertical_goal = -v_descent
descent_error   = clamp01(abs(verticalSpeed - v_vertical_goal)
                          / max(landingDescentErrorMinimumScaleMps, v_descent))
chopstick_closure = clamp(goalClosureRate
                          / max(landingClosureMinimumScaleMps, v_descent), -1, 1)
leg_closure       = clamp(horizontalClosureRate
                          / landingClosureMinimumScaleMps, -1, 1)
planar_error    = 1 - Exp01(planarDistance, landingPlanarDistanceFalloffM)
upright_error   = 1 - upright01
near_target     = Exp01(h, landingNearTargetAltitudeFalloffM)
planar_speed    = clamp01(planarSpeed / landingPlanarSpeedScaleMps)
angular_rate    = clamp01(angularRateDegS / landingAngularRateScaleDegS)
```

The common rate is:

```text
+ landingGoalClosureRewardRate             * task_closure
- landingDescentProfileErrorCostRate        * descent_error
- landingPlanarDistanceCostRate             * planar_error
- landingUprightErrorCostRate               * upright_error
- landingNearTargetPlanarSpeedCostRate      * near_target * planar_speed
- landingNearTargetAngularRateCostRate      * near_target * angular_rate
- controlEffortCostRate                     * clamp01(controlEffort)
- timeCostRate
```

The desired vertical speed naturally approaches zero at ground level. Leg
landing deliberately uses horizontal closure so passive vertical fall cannot
pay the navigation signal; chopstick capture retains 3D closure. The model does
not prescribe throttle, engine count, or ignition time. PPO must discover the
controls needed to follow the profile.

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
vertical_excess = clamp01((abs(verticalSpeed) - current_vertical_limit)
                          / landingVerticalSpeedExcessScaleMps)
upward_excess = clamp01((verticalSpeed - landingUpwardVelocityToleranceMps)
                         / landingUpwardVelocityScaleMps)
upright_error = near_target
                * clamp01(tiltDeg / max(6 deg, 2 * current_tilt_limit))
center_quality = Exp01(planarDistance, landingPlanarDistanceFalloffM)

- landingYawSpinCostRate * yaw_spin
- landingNearTargetVerticalSpeedCostRate * near_target * vertical_excess
- landingAngularRateCostRate * angular_rate
- landingUpwardVelocityCostRate * upward_excess
+ landingReadinessProgressRewardRate * d(center_quality)/dt
```

This body-axis term prevents the policy from using fins to spin around the
rocket's vertical axis while still appearing upright. Direct tilt shaping is
gated by height, so the vehicle can lean to navigate aloft but is increasingly
pressed upright through the flare. The bounded progress potential contains only
center quality. Descent-profile, horizontal-speed, tilt, and angular-rate terms
remain independent, so simply falling closer to the altitude gate cannot create
positive progress while the vehicle drifts away. Center progress is disabled
after impact. First contact pays no
milestone. A flat `+2` event is emitted once when four-foot, propulsion-off
support completes its required hold.

Balanced terminal magnitudes are:

| Outcome | Reward or cost |
|---|---:|
| Successful touchdown | +7.5 to +30 from impact/center quality |
| Successful mission efficiency | up to +4 at every difficulty; failed attempts receive zero |
| Hard touchdown, structural strike, excessive rebound | -25 to -45 by impact severity |
| Flyaway, fuel depletion, altitude escape, missed pad | -50 |
| Timeout | -50 |
| Foot outside pad | disabled by default; optional rule costs -25 to -45 |
| Airborne unsafe attitude | disabled by default; attitude remains densely shaped |

For a legal stable touchdown, first-contact quality combines translational
speed, tilt, and angular rate inside the current curriculum envelope. Center
quality falls smoothly from 1 at the pad center to 0 at the current success
radius. Their geometric mean controls 75% of the `+30` reward; the remaining
25% keeps a marginal but legal landing positive. Mission efficiency is a
separate success-only tie-breaker and does not constrain which engines the
policy may use:

```text
fuel_used_fraction = clamp01(1 - fuelRemainingFraction)
additional_first_ignitions = max(0, distinctIgnitedEngineChannels - 1)
mission_cost = fuel_used_fraction
             + engineRestartCount * restartEquivalentFuelFraction
             + additional_first_ignitions
               * additionalEngineIgnitionEquivalentFuelFraction
cost_scale = lerp(initialMissionCostScale,
                  fullMissionCostScale,
                  curriculumDifficulty)
mission_efficiency = exp(-mission_cost / cost_scale)
success_bonus = successfulMissionEfficiencyReward
                * mission_efficiency
                * efficiencyCurriculumGate
```

Balanced uses cost scales `0.20 -> 0.28`, a `0.003` equivalent-fuel charge per
restart, and only `0.0005` for each additional engine channel's first ignition.
Fuel therefore dominates, a one-to-three-engine braking transition is cheap,
and repeated shutdown/relight PWM is appreciably worse. The exponential does
not clip at a budget, so reducing mission cost remains useful for every legal
landing.

The Balanced leg objective has no per-second time cost: efficiency is judged
only after a legal landing, so ending a failed episode quickly is not rewarded.
For contact failures, the worst first-contact measurement is divided by its
active success limit. Impact severity rises linearly from zero at `1x` to its
full `-20` at `3x`. A typical `1.5x` L6 impact therefore costs `-30`, rather than
about `-12`, while every physical contact failure remains capped at `-45` and
every non-contact escape or timeout costs `-50`. This keeps a real attempt
preferable without making a repeatable crash the cheap outcome or prescribing
how many engines may fire.

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

Landing stability criteria define `stableCaptureReward` while that magnitude is
nonzero. Leg touchdown limits define the safe contact-quality and foot-support
progress events independently of whether stable touchdown termination is
enabled. Moving-target capture criteria always define target-capture events,
target movement, and curriculum accounting even when capture-count termination
is disabled. Their thresholds therefore remain active and validated for those
non-terminal roles.

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

1. physical impact outside the designated pad (missed pad)
2. structural strike on the pad
3. foot outside the pad
4. hard first contact
5. excessive rebound or sustained loss of all foot contacts
6. stable touchdown success
7. settled four-foot support outside the active center radius (missed pad)
8. unsafe attitude
9. horizontal flyaway
10. fuel depletion
11. altitude escape above the episode start
12. geometric missed pad
13. time limit

Hard first contact and stable touchdown use the interpolated landing envelope.
Default stable touchdown additionally requires all four feet, main engines and
RCS physically off, and an uninterrupted hold from 0.4 to 1.0 seconds. The
first external physical impact latches the one-way propulsion interlock, so the
policy cannot relight after either pad or terrain contact. A non-pad impact
terminates immediately at the fixed missed-pad cost and cannot create foot
support or earn a pad-contact event. A calm four-foot landing outside the
active center radius also terminates as missed-pad after the same short hold;
it no longer waits for the global timeout. Rebound measurements are sticky after first contact and use
`legMaximumReboundRiseM` and `legMaximumAllFeetContactLossSeconds`.

The default leg objective leaves priority item 8 disabled and uses a 60-second
limit. It remains in the configurable list for deliberate custom experiments.

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

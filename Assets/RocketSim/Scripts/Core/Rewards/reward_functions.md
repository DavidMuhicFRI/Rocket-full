# RocketSim Reward Function Guide

This document explains the reward received by the agent at each physics
timestep. The executable implementation is:

`Assets/RocketSim/Scripts/Core/Rewards/RocketRewardModel.cs`

The goal of these rewards is not to hand the agent a perfect answer. The reward
is a set of small hints that make useful behavior easier to discover: stay near
the goal, stay upright when the scenario needs it, move calmly, avoid wasteful
controls, and terminate clearly when the flight is unrecoverable.

## How To Read The Formulas

Each scenario produces a shaping **rate** every physics timestep. The agent
multiplies that rate by `Time.fixedDeltaTime` before adding it, so changing the
physics frequency does not change the shaping return merely by creating more
steps per simulated second. Terminal rewards and penalties are one-off signals
added to the final timestep; they do not overwrite shaping accumulated since
the previous ML-Agents decision.

The helper `clamp01(value)` means:

```text
clamp01(value) = min(max(value, 0), 1)
```

The helper `closeness(value, scale)` means:

```text
closeness(value, scale) = exp(-abs(value) / max(scale, 0.0001))
```

This returns a value near `1` when `value` is close to zero, and smoothly falls
toward `0` as the error grows. The `scale` controls how forgiving the reward is.
For example, `closeness(horizontal_error_m, 8)` is fairly strict, while
`closeness(horizontal_error_m, 15)` is more forgiving.

## Shared Names

These names match the concepts used by `RocketRewardModel.cs`, but are written
to be easier to read than compact math symbols.

```text
goal_error_3d_m          = full 3D distance from rocket to scenario goal
horizontal_error_m       = XZ-plane distance from rocket to target/pad
vertical_error_m         = rocket height minus target height
speed_mps                = full 3D rocket speed
horizontal_speed_mps     = XZ-plane rocket speed
vertical_speed_mps       = upward/downward speed
goal_closure_rate_mps    = positive when moving toward the goal
horizontal_closure_rate_mps = positive when moving horizontally toward target
upright_dot              = dot(rocket_up, world_up), where 1 is upright
upright_score            = clamp01((upright_dot + 1) / 2)
angular_rate_deg_s       = total body angular speed
yaw_error_deg            = shortest heading error to the configured chopstick yaw
control_effort           = mean(throttle)
                         + 0.05 * mean_abs(gimbal_degrees)
                         + 0.02 * mean_abs(fin_degrees)
```

The control effort weights are intentionally small. They discourage frantic
control use without preventing the agent from using throttle, gimbal, or fins
when those controls are necessary.

The reward model also receives runtime context:

```text
altitude_m               = rocket altitude
ground_clearance_m       = base clearance threshold used for contact/landing
fuel_kg                  = remaining fuel
hover_track_radius_m     = current hover-tracking settle radius from curriculum
```

In simple terms, reward terms describe what the rocket is doing, while runtime
context describes the current scenario limits needed to judge that behavior.

## Configurable Reward Factors

The formulas below show the built-in baseline weights. At runtime, the Scenario
tab exposes scenario-specific reward factors that multiply the relevant baseline
terms. A factor of `1.0` keeps the baseline behavior, values above `1.0` make
that objective stronger, and values below `1.0` make it weaker.

The exposed factors are grouped by intent rather than by every individual
coefficient: target precision, altitude hold, uprightness, speed discipline,
vertical profile, vertical calm, attitude calm, control efficiency, ascent
drive, approach drive, settle precision, belly attitude, and terminal signal.
Presets in the UI set these factors to useful starting points, and moving any
slider marks that scenario reward model as custom.

## Landing

Goal: guide the upper CatchFrame into the simulated chopstick envelope upright,
slow, and aligned with the target yaw.

```text
height_above_touchdown_m = max(0, altitude_m - ground_clearance_m)
touchdown_speed_scale = sqrt(gravity * ground_clearance_m)
desired_descent_speed_mps = 0.30 * sqrt(2 * gravity * height_above_touchdown_m)
desired_vertical_speed_mps = -desired_descent_speed_mps
speed_tolerance = max(touchdown_speed_scale, 0.45 * desired_descent_speed_mps)

vertical_profile_score =
    2 * closeness(vertical_speed_mps - desired_vertical_speed_mps, speed_tolerance) - 1

closure_scale = max(touchdown_speed_scale, desired_descent_speed_mps + speed_tolerance)
goal_closure_score = clamp01(goal_closure_rate_mps / closure_scale)
wrong_way_score = clamp01(max(0, -goal_closure_rate_mps) / closure_scale)
near_ground_score = 1 - clamp01(height_above_touchdown_m / 50)
center_precision_score = closeness(horizontal_error_m, max(0.5, success_radius_m * 0.5))
heading_precision_score = closeness(yaw_error_deg, max(1, success_yaw_error_deg))

reward =
  0.10 * closeness(horizontal_error_m, 12)
+ 0.10 * near_ground_score * center_precision_score
+ 0.06 * upright_score
+ 0.04 * closeness(angular_rate_deg_s, 60)
+ 0.06 * near_ground_score * heading_precision_score * upright_score
+ 0.16 * vertical_profile_score
+ 0.05 * goal_closure_score
- 0.05 * wrong_way_score
- 0.0020 * horizontal_error_m
- 0.0007 * angular_rate_deg_s
- 0.0030 * control_effort
- 0.0060
```

Terminal rules:

```text
if upright_dot < 0.35 or horizontal_error_m > 150 or fuel_kg <= 0:
    reward = -35

if altitude_m > actual_episode_start_altitude_m + 100:
    reward = -30

if altitude_m <= ground_clearance_m:
    touchdown_speed_limit = curriculum_total_touchdown_speed_mps
    touchdown_vertical_speed_limit = curriculum_vertical_touchdown_speed_mps
    touchdown_horizontal_speed_limit = curriculum_horizontal_touchdown_speed_mps

    good_landing =
        upright_dot > 0.94
        and upright_dot > cos(curriculum_tilt_deg)
        and horizontal_error_m < curriculum_success_radius_m
        and speed_mps < touchdown_speed_limit
        and abs(vertical_speed_mps) < touchdown_vertical_speed_limit
        and horizontal_speed_mps < touchdown_horizontal_speed_limit
        and angular_rate_deg_s < curriculum_angular_rate_deg_s
        and yaw_error_deg < curriculum_yaw_error_deg

    precision_bonus = 12 * center_precision_score + 8 * heading_precision_score
    reward = 35 + precision_bonus if good_landing else -25
```

Why these values:

- `horizontal_error_m` uses a 12 m closeness scale because early landing needs a
  broad attraction basin. The hard terminal success threshold is 2 m, so the
  shaping reward guides the agent before it is precise enough to land.
- The landing reward now uses one altitude-derived vertical-speed profile. The
  desired downward speed is based on current height and gravity, so it naturally
  becomes slower near touchdown.
- Upward flight is not a separate special case in shaping. It is penalized
  because positive vertical speed is far away from the desired downward speed.
- A small per-step cost makes delaying touchdown less profitable than completing
  the landing.
- `upright_dot > 0.94` is the broad final landing posture gate, then the
  curriculum tilt limit tightens it from 20 degrees to 5 degrees. `upright_dot
  < 0.35` is treated as unrecoverable. The gap gives the agent room to correct
  during descent.
- A successful touchdown is worth `35` plus up to `12` for exact pad centering
  and up to `8` for exact chopstick heading. Landing at the edge of the allowed
  radius therefore succeeds but earns less than landing in the center.
- Heading is represented by sine and cosine of the signed yaw error in the v2
  observation schema. Saved v1 models keep their original input size and skip
  heading reward and the heading success gate; they must be retrained to learn
  chopstick alignment.

Landing curriculum:

Completed episodes update a 32-episode recent-success estimate. Curriculum
movement happens once per parallel-area-sized batch, so increasing the number
of training areas does not make difficulty change faster. The default Adaptive
mode uses hysteresis:

```text
if recent_success_rate > 0.80:
    move difficulty forward
else if recent_success_rate < 0.50:
    move difficulty backward at half speed
else:
    hold difficulty

maximum change per batch = 0.02
lowest allowed retreat = best linear progress reached - 0.20
curriculum_progress = smoothstep(0, 1, clamp01(linear_progress))
```

Monotonic mode uses the same forward rule but disables retreat. Fixed Full
Difficulty holds `curriculum_progress = 1` and acts as the no-curriculum
comparison condition. Fifteen percent of curriculum episodes replay the full
task profile from `0.20` lower difficulty. Replay episodes train the policy but
do not update the mastery estimate. Each agent freezes its selected profile for
the entire episode so shared updates from parallel agents cannot change a task
halfway through.

The current difficulty is one continuous curve. It changes the complete task
profile rather than switching between named easy/hard stages:

```text
spawn_altitude_m          = lerp(random 120..220, random 300..1000, progress)
spawn_horizontal_offset_m = lerp(8,               100,              progress)
spawn_down_speed_mps      = lerp(random 5..20,    random 20..120,   progress)
spawn_horizontal_speed    = lerp(1,               25,               progress)
spawn_pitch_roll_deg      = lerp(3,               18,               progress)
spawn_angular_speed_deg_s = lerp(0,               55,               progress)
spawn_yaw_range_deg       = lerp(30,              180,              progress)

success_radius_m          = lerp(8,               2,                progress)
success_total_speed_mps   = lerp(7,               2.5,              progress)
success_vertical_speed    = lerp(5,               2,                progress)
success_horizontal_speed  = lerp(5,               1,                progress)
success_tilt_deg          = lerp(20,              5,                progress)
success_angular_rate_deg  = lerp(50,              25,               progress)
success_yaw_error_deg     = lerp(30,              10,               progress)
capture_half_size_m       = lerp(8,               3,                progress)
stable_hold_seconds       = lerp(0.15,            0.45,             progress)
```

The spawn sampler then clamps sampled motion to the Falcon 9-like booster's
recoverable braking and lateral-control envelope. The platform is a visual and
logical capture volume from the first curriculum episode. It has no collision
surfaces: capture is simulated from CatchFrame position, velocity, attitude,
yaw, and stable time inside the volume.

Flyaway termination is episode-relative. Moving more than 150 m horizontally
from the target or climbing more than 100 m above the sampled CatchFrame start
height ends the episode. A high curriculum spawn is therefore valid, while the
early-training "boost upward" failure mode still terminates promptly.

## Hover

Goal: hold a fixed altitude around 30 m while remaining upright and controlled.

```text
reward =
  0.12 * closeness(abs(vertical_error_m), 5)
+ 0.12 * closeness(horizontal_error_m, 8)
+ 0.07 * upright_score
+ 0.06 * closeness(speed_mps, 4)
+ 0.04 * closeness(angular_rate_deg_s, 35)
- 0.0015 * speed_mps
- 0.0008 * angular_rate_deg_s
- 0.0025 * control_effort
```

Terminal rule:

```text
if altitude_m < ground_clearance_m
or upright_dot < 0.45
or fuel_kg <= 0
or horizontal_error_m > 80
or altitude_m > 200:
    reward = -10
```

Why these values:

- Hover gives equal top weight to vertical and horizontal position (`0.12` each)
  because stable hover requires both height control and pad centering.
- The vertical scale is 5 m because hover should be fairly tight in altitude.
  The horizontal scale is 8 m because sideways correction is harder and should
  be rewarded before it is perfect.
- Speed gets a strict 4 m/s scale because hover should settle, not orbit or
  drift around the target.
- The 80 m horizontal and 200 m altitude limits are generous failure bounds.
  They stop wasted episodes after the vehicle has clearly flown away.

## Hover Tracking

Goal: travel toward a moving target pad when far away, then slow down and hold a
stable hover once close to it.

```text
hover_phase_radius_m = max(hover_track_radius_m, 1)
hover_phase = horizontal_error_m <= hover_phase_radius_m

base_hover_reward =
      0.12 * closeness(abs(vertical_error_m), 5)
    + 0.12 * closeness(horizontal_error_m, 8)
    + 0.07 * upright_score
    + 0.06 * closeness(speed_mps, 4)
    + 0.04 * closeness(angular_rate_deg_s, 35)
    - 0.0015 * speed_mps
    - 0.0008 * angular_rate_deg_s
    - 0.0025 * control_effort

if hover_phase:
    tight_center_score = closeness(horizontal_error_m, max(2, hover_phase_radius_m * 0.5))
    horizontal_calm_score = closeness(horizontal_speed_mps, 1.8)
    vertical_calm_score = closeness(abs(vertical_speed_mps), 1.4)
    rotational_calm_score = closeness(angular_rate_deg_s, 28)

    reward = base_hover_reward
    reward +=
      0.045 * tight_center_score
    + 0.035 * horizontal_calm_score
    + 0.025 * vertical_calm_score
    + 0.020 * rotational_calm_score
    + 0.020 * tight_center_score * horizontal_calm_score * vertical_calm_score * upright_score
    - 0.0005 * horizontal_speed_mps
    - 0.0004 * abs(vertical_speed_mps)
else:
    distance_beyond_hover_m = max(0, horizontal_error_m - hover_phase_radius_m)
    approach_speed_score = clamp01(horizontal_closure_rate_mps / 5)
    moving_away_speed_mps = max(0, -horizontal_closure_rate_mps)

    if horizontal_speed_mps > 0.1:
        direction_score =
            clamp01((horizontal_closure_rate_mps / max(horizontal_speed_mps, 0.001) + 1) / 2)
    else:
        direction_score = 0

    useful_speed_score = clamp01(horizontal_speed_mps / 5)
    far_from_hover_score = clamp01(distance_beyond_hover_m / max(hover_phase_radius_m, 1))
    overspeed_penalty = clamp01(max(0, horizontal_speed_mps - 8) / 6)

    reward = base_hover_reward
    reward +=
      0.085 * approach_speed_score
    + 0.040 * direction_score
    + 0.020 * useful_speed_score
    - 0.045 * clamp01(moving_away_speed_mps / 4)
    - 0.025 * far_from_hover_score * (1 - useful_speed_score)
    - 0.020 * overspeed_penalty
```

Terminal rule:

```text
if altitude_m < ground_clearance_m
or upright_dot < 0.45
or fuel_kg <= 0
or horizontal_error_m > 90
or altitude_m > 200:
    reward = -10
```

Extra hover-tracking cycle reward:

This part is handled by `FalconAgent.cs`, not by `RocketRewardModel.cs`, but it
adds reward on top of the shaping reward when the tracking task is completed.

```text
success_radius_m = max(hover_track_radius_m, 0.5)
success_max_speed_mps = max(config_success_max_speed_mps, 0.1)
success_max_tilt_deg = max(config_success_max_tilt_deg, 0.1)

hover_ready =
    horizontal_error_m <= success_radius_m
    and abs(vertical_error_m) <= 3
    and horizontal_speed_mps <= success_max_speed_mps
    and abs(vertical_speed_mps) <= success_max_speed_mps
    and tilt_deg <= success_max_tilt_deg
    and angular_rate_deg_s <= 25

if hover_ready stays true for max(0, success_hold_seconds):
    reward += 4
    move the target pad to a new random position
```

Automatic curriculum:

This part is handled by `SimEnvironmentConfig.cs`. It changes the hover-tracking
task difficulty from completed episode outcomes. The Scenario tab exposes only
the difficulty increase speed; movement, settling, hold, speed, and tilt ranges
are standardized.

```text
recent_success_rate = smoothed_average(episode_captured_any_pad)
advance_pressure = inverse_lerp(0.55, 0.85, recent_success_rate)
linear_progress += advance_pressure * difficulty_increase_speed / (base_batches_to_full * active_training_areas)
curriculum_progress = smoothstep(0, 1, clamp01(linear_progress))

pad_move_radius_m       = lerp(16, 50,  curriculum_progress)
hover_phase_radius_m    = lerp(10, 2.5, curriculum_progress)
success_hold_seconds    = lerp(0.5, 2,  curriculum_progress)
success_max_speed_mps   = lerp(3, 0.8,  curriculum_progress)
success_max_tilt_deg    = lerp(15, 5,   curriculum_progress)
```

Why these values:

- `hover_phase_radius_m` is the split point. Outside it, the reward cares most
  about moving toward the pad. Inside it, the reward changes personality and
  cares most about settling.
- The curriculum advances from recent successful-episode rate, not from a timer
  or individual pad captures. This keeps target changes frequent without letting
  one good episode spike difficulty.
- Difficulty increases in two directions at once: the pad can move farther away,
  and the definition of a successful hover becomes tighter.
- Hover-track starts from the same reward as the fixed hover task. This keeps
  altitude control, uprightness, calm speed, and low angular rate as the default
  behavior in both phases.
- The moving phase only adds modest optimization terms for useful translation:
  close horizontal distance, point velocity toward the pad, avoid moving away,
  and avoid hovering motionless while still far from the pad.
- The moving phase uses horizontal closure speed, not 3D closure speed, because
  the tracking task is mainly X/Z pad tracking while altitude should remain
  stable.
- The hover phase adds small tighter-settling terms on top of the hover reward.
  Once close to the pad, the policy is optimized for centering and calm motion,
  but it is not learning a totally different reward function.
- A successful held hover gives a separate `+4` cycle-complete reward. This is
  the crisp signal for the whole behavior: travel to the moved pad, stabilize,
  hold it, then receive the next target.
- `horizontal_speed_mps` is rewarded directly in the hover phase because average
  distance alone does not tell whether the rocket is calming down. A good policy
  should show both low horizontal error and falling horizontal speed.
- The pad now moves after a successful held hover instead of after a timer. This
  avoids moving the target before the rocket has demonstrated control, and it
  turns each pad move into a clear skill checkpoint.
- Progress plots should not rely only on raw horizontal error or raw travel
  time. As `pad_move_radius_m` grows, raw travel time can stay flat or rise even
  when the policy improves. Instead, track direction efficiency, horizontal
  closure rate, distance-normalized travel rate, travel progress, and relative
  settle quality.
- The failure radius remains 90 m because the target can jump around inside a
  curriculum radius, so tracking episodes need more tolerance than static hover.

## Takeoff

Goal: lift off upright, climb past 120 m, and avoid lateral drift.

```text
altitude_progress = clamp01((altitude_m - ground_clearance_m) / 100)
upward_speed_score = clamp01(vertical_speed_mps / 20)

reward =
  0.10 * altitude_progress
+ 0.08 * upward_speed_score
+ 0.08 * upright_score
+ 0.08 * closeness(horizontal_error_m, 10)
+ 0.04 * closeness(angular_rate_deg_s, 45)
- 0.0015 * horizontal_error_m
- 0.0008 * angular_rate_deg_s
- 0.0020 * control_effort
```

Terminal rules:

```text
if upright_dot < 0.45 or horizontal_error_m > 80:
    reward = -10

if altitude_m > 120:
    good_takeoff =
        upright_dot > 0.90
        and horizontal_error_m < 12
        and angular_rate_deg_s < 45

    reward = 10 if good_takeoff else 4

if fuel_kg <= 0:
    reward = -5
```

Why these values:

- `altitude_progress` reaches full value over roughly 100 m. This gives steady
  climb feedback before the 120 m terminal condition.
- `upward_speed_score` is capped at 20 m/s. Faster ascent is useful, but only up
  to a point; beyond that the agent should care more about staying controlled.
- Horizontal error still matters during takeoff because a policy that simply
  rockets upward while drifting sideways is not useful.
- Reaching 120 m gets at least `+4` even if imperfect, because climbing is the
  main objective. A clean, upright, centered ascent gets `+10`.
- Fuel exhaustion is `-5`, less severe than tipping or flying away, because in
  takeoff the agent may still have made partial progress before running out.

## Belly Flop

Goal: descend broadside at high altitude, then rotate upright for landing.

```text
reentry_phase = clamp01((altitude_m - 55) / 45)
landing_phase = 1 - reentry_phase
belly_attitude_score = 1 - abs(upright_dot)

reward =
  0.08 * reentry_phase * belly_attitude_score
+ 0.08 * landing_phase * upright_score
+ 0.08 * closeness(horizontal_error_m, 15)
+ 0.05 * landing_phase * closeness(speed_mps, 6)
+ 0.04 * closeness(angular_rate_deg_s, 70)
- 0.0015 * horizontal_error_m
- 0.0010 * landing_phase * speed_mps
- 0.0006 * angular_rate_deg_s
- 0.0020 * control_effort
```

Additional penalty:

```text
if altitude_m > 40 and upright_dot > 0.85:
    reward = reward - 0.04
```

Terminal rules:

```text
if altitude_m <= ground_clearance_m:
    good_landing =
        upright_dot > 0.90
        and horizontal_error_m < 8
        and speed_mps < 5
        and angular_rate_deg_s < 45

    reward = 12 if good_landing else -10

if fuel_kg <= 0 or horizontal_error_m > 150:
    reward = -10
```

Why these values:

- `reentry_phase` is high above about 55 m and fades out below that. This makes
  the agent prefer a horizontal belly-flop attitude high up.
- `landing_phase` is the opposite. As the rocket gets low, upright posture and
  low speed become more important.
- The horizontal scale is 15 m, looser than hover, because the belly-flop task
  involves a dramatic attitude transition and descent before final precision.
- Angular rate uses a 70 deg/s scale, also looser than hover, because the flip
  maneuver requires more rotation than a steady hover.
- The high-altitude upright penalty discourages the agent from skipping the
  belly-flop phase and just falling upright.
- Final touchdown is strict: the scenario can tolerate messy motion during the
  flip, but the landing itself must be upright, near the pad, slow, and calm.

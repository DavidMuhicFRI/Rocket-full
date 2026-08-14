"""
Plot RocketSim telemetry CSVs.

Episode CSVs are aggregated across training areas:
    line     = mean value for that episode across all areas
    envelope = +/- one standard deviation across those areas

Step CSVs are plotted as control-response diagnostics instead of averaged
timelines, so they show how the learned policy reacts to navigation and
attitude state.
"""

from __future__ import annotations

import argparse
import math
import textwrap
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd


# Change this to your CSV path. Command-line argument overrides it.
TELEMETRY_CSV = r"C:\Users\dadii\AppData\LocalLow\DefaultCompany\Falcon9\Telemetry\telemetry_HoverTargetPrecision_episodes.csv"

# Plots are saved next to the CSV by default.
OUTPUT_DIR = ""

# For very large step files, keep profile plots responsive by sampling rows.
MAX_STEP_ROWS = 120_000

# Rolling smoothing for raw step timelines only.
STEP_SMOOTH_WINDOW = 250
STEP_PROFILE_BINS = 100
BOOTSTRAP_CHECK_EPISODES = 1

FIXED_DELTA_TIME = 0.02

ANGLE_METRIC_TOKENS = ("BearingDeg", "ErrorDeg")
EPISODE_X_LABEL = "Episode index (completed training episodes)"
STEP_PROGRESS_X_LABEL = "Episode progress (% of timesteps within each episode)"

TELEMETRY_ALIASES = {
    "Goal_Distance3D_m": ("Goal_DistanceToGoal3D_m",),
    "Goal_PlanarDistance_m": ("Goal_HorizontalDistanceToGoal_m",),
    "Goal_VerticalError_m": ("Goal_VerticalDistanceToGoal_m",),
    "Goal_AbsVerticalError_m": ("Goal_AbsoluteVerticalDistanceToGoal_m",),
    "Track_PhaseHover01": ("Track_IsHoverPhase01",),
    "Track_HoverReady01": ("Track_IsHoverReady01",),
    "Track_StableTime_s": ("Track_StableHoverTime_s",),
    "Track_TargetReached01": ("Track_TargetCaptured01",),
    "Track_SettleRadius_m": ("Track_HoverRadius_m",),
    "Track_CurriculumProgress01": ("Track_CurriculumDifficulty01",),
    "Track_SegmentStartDistance_m": ("Track_TargetJumpDistance_m",),
    "Track_SegmentElapsed_s": ("Track_TargetElapsedTime_s",),
    "Track_TravelProgressRate_1ps": ("Track_NormalizedTravelRate_1ps",),
    "Track_DirectionEfficiency01": ("Track_DirectionAccuracy01",),
    "Track_SettleQuality01": ("Track_StabilizationQuality01",),
    "State_Altitude_m": ("State_RocketAltitude_m",),
    "Nav_TargetBearingDeg": ("Nav_BearingToGoalDeg",),
    "Nav_VelocityBearingDeg": ("Nav_HorizontalVelocityBearingDeg",),
    "Nav_VelocityTargetErrorDeg": ("Nav_VelocityDirectionErrorToGoalDeg",),
    "Nav_GoalAlignment": ("Nav_VelocityAlignmentToGoal",),
    "Nav_GimbalTargetErrorDeg": ("Nav_GimbalDirectionErrorToGoalDeg",),
    "Vel_PlanarSpeed_mps": ("Vel_HorizontalSpeed_mps",),
    "Vel_GoalClosureRate_mps": ("Vel_ClosureSpeedToGoal_mps",),
    "Vel_HorizontalClosureRate_mps": ("Vel_HorizontalClosureSpeedToGoal_mps",),
    "Ctrl_ThrottleMean": ("Ctrl_MeanThrottle01",),
    "Ctrl_ThrottleMax": ("Ctrl_MaxThrottle01",),
    "Ctrl_GimbalMeanAbsDeg": ("Ctrl_MeanGimbalDeflectionDeg",),
    "Ctrl_FinMeanAbsDeg": ("Ctrl_MeanFinDeflectionDeg",),
    "Fuel_Fraction": ("Fuel_RemainingFraction01",),
    "Load_GForce": ("Load_LoadFactorG",),
    "Load_AngularAccelDegS2": ("Load_AngularAccelerationDegS2",),
    "Load_DynPressurePa": ("Load_DynamicPressurePa",),
    "Env_WindPlanarSpeed_mps": ("Env_HorizontalWindSpeed_mps",),
    "Env_WindAlignment": ("Env_WindVelocityAlignment",),
    "Env_DynPressureNorm": ("Env_NormalizedDynamicPressure01",),
    "Reward_Step": ("Reward_StepReward",),
}

ALIAS_TO_CANONICAL = {
    alias: canonical
    for canonical, aliases in TELEMETRY_ALIASES.items()
    for alias in aliases
}


EPISODE_GROUPS = [
    (
        "01_goal_tracking",
        "Goal / Position Tracking",
        [
            ("Goal_Distance3D_m", "Goal distance 3D (m)"),
            ("Goal_PlanarDistance_m", "Horizontal error (m)"),
            ("Goal_AbsVerticalError_m", "Absolute vertical error (m)"),
            ("Track_PhaseHover01", "Hover phase fraction (0..1)"),
            ("Track_HoverReady01", "Hover-ready fraction (0..1)"),
            ("Track_StableTime_s", "Stable hover time (s)"),
            ("State_Altitude_m", "Rocket altitude (m)"),
        ],
    ),
    (
        "02_navigation_bearings",
        "Navigation Direction",
        [
            ("Nav_TargetBearingDeg", "Target bearing (deg)"),
            ("Nav_VelocityBearingDeg", "Velocity bearing (deg)"),
            ("Nav_VelocityTargetErrorDeg", "Velocity-target error (deg)"),
            ("Nav_GoalAlignment", "Goal alignment (-1..1)"),
            ("Nav_GimbalBearingDeg", "Gimbal bearing (deg)"),
            ("Nav_GimbalTargetErrorDeg", "Gimbal-target error (deg)"),
        ],
    ),
    (
        "03_attitude_rotation",
        "Attitude / Rotation Stability",
        [
            ("Att_TiltDeg", "Tilt (deg)"),
            ("Att_AngularRateDegS", "Angular rate (deg/s)"),
            ("Att_TiltRateDegS", "Tilt rate (deg/s)"),
            ("Att_PitchRateDegS", "Pitch rate (deg/s)"),
            ("Att_YawRateDegS", "Yaw rate (deg/s)"),
            ("Att_RollRateDegS", "Roll rate (deg/s)"),
            ("Aero_AoaDeg", "Angle of attack (deg)"),
        ],
    ),
    (
        "04_velocity",
        "Velocity Control",
        [
            ("Vel_Speed3D_mps", "3D speed (m/s)"),
            ("Vel_PlanarSpeed_mps", "Horizontal speed (m/s)"),
            ("Vel_VerticalSpeed_mps", "Vertical speed (m/s)"),
            ("Vel_GoalClosureRate_mps", "Goal closure rate (m/s)"),
            ("Vel_AirRelativeSpeed_mps", "Air-relative speed (m/s)"),
        ],
    ),
    (
        "05_control_fuel",
        "Control Effort / Fuel",
        [
            ("Ctrl_ThrottleMean", "Mean throttle (0..1)"),
            ("Ctrl_ThrottleMax", "Max throttle (0..1)"),
            ("Ctrl_GimbalMeanAbsDeg", "Gimbal effort (deg)"),
            ("Ctrl_FinMeanAbsDeg", "Fin effort (deg)"),
            ("Fuel_Fraction", "Fuel remaining fraction (0..1)"),
            ("Fuel_UsedKg", "Fuel used (kg)"),
        ],
    ),
    (
        "06_aero_loads",
        "Aero / Loads / Safety",
        [
            ("Load_GForce", "Load factor (g)"),
            ("Load_AngularAccelDegS2", "Angular accel (deg/s^2)"),
            ("Load_DynPressurePa", "Dynamic pressure (Pa)"),
            ("Thermal_HeatFluxWm2", "Heat flux (W/m^2)"),
            ("Stress_BendingPa", "Bending stress (Pa)"),
            ("Stress_AxialPa", "Axial stress (Pa)"),
        ],
    ),
    (
        "07_reward",
        "Reward Signal",
        [
            ("Reward_Step", "Step reward"),
        ],
    ),
    (
        "08_hover_track_curriculum",
        "Hover-Track Curriculum Progress",
        [
            ("Track_CurriculumProgress01", "Curriculum progress (0..1)"),
            ("Track_SegmentStartDistance_m", "Pad jump distance (m)"),
            ("Track_SettleRadius_m", "Required hover radius (m)"),
            ("Track_TravelProgressRate_1ps", "Distance-normalized travel rate (1/s)"),
            ("Track_DirectionEfficiency01", "Direction efficiency (0..1)"),
            ("Track_SettleQuality01", "Relative settle quality (0..1)"),
        ],
    ),
]


STEP_TIMESERIES = [
    ("Goal_PlanarDistance_m", "Horizontal error (m)"),
    ("Vel_HorizontalClosureRate_mps", "Horizontal closure speed (m/s)"),
    ("Vel_PlanarSpeed_mps", "Horizontal speed (m/s)"),
    ("Track_TravelProgress01", "Travel progress (0..1)"),
    ("Track_DirectionEfficiency01", "Direction efficiency (0..1)"),
    ("Track_SettleQuality01", "Relative settle quality (0..1)"),
    ("Track_HoverReady01", "Hover-ready (0/1)"),
    ("Track_StableTime_s", "Stable hover time (s)"),
    ("Att_TiltDeg", "Tilt (deg)"),
    ("Reward_Step", "Step reward"),
]


STEP_EPISODE_GROUPS = [
    (
        "01_step_hover_track_outcomes",
        "Hover-Track Outcomes From Step Telemetry",
        [
            ("CycleCompletions", "Completed travel+stabilize cycles"),
            ("CycleRate_per_min", "Cycles per minute"),
            ("MeanStepReward", "Mean step reward"),
            ("EpisodeDuration_s", "Episode duration (s)"),
            ("MaxStableHoverTime_s", "Max stable hover time (s)"),
        ],
    ),
    (
        "02_step_hover_track_travel",
        "Hover-Track Travel Quality",
        [
            ("TargetJumpDistance_m", "Target jump distance (m)"),
            ("MovingHorizontalClosureSpeed_mps", "Moving-phase closure speed (m/s)"),
            ("MovingDirectionAccuracy01", "Moving-phase direction accuracy (0..1)"),
            ("MovingNormalizedTravelRate_1ps", "Moving-phase normalized travel rate (1/s)"),
            ("FinalTravelProgress01", "Final travel progress (0..1)"),
        ],
    ),
    (
        "03_step_hover_track_stabilization",
        "Hover-Track Stabilization Quality",
        [
            ("HoverReadyFraction01", "Hover-ready fraction (0..1)"),
            ("HoverStabilizationQuality01", "Hover-phase stabilization quality (0..1)"),
            ("HoverHorizontalSpeed_mps", "Hover-phase horizontal speed (m/s)"),
            ("HoverAbsVerticalSpeed_mps", "Hover-phase vertical speed magnitude (m/s)"),
            ("HoverTiltDeg", "Hover-phase tilt (deg)"),
        ],
    ),
    (
        "04_step_position_motion",
        "Position And Motion From Step Telemetry",
        [
            ("MeanHorizontalDistanceToGoal_m", "Mean horizontal distance to goal (m)"),
            ("FinalHorizontalDistanceToGoal_m", "Final horizontal distance to goal (m)"),
            ("MeanAbsVerticalDistanceToGoal_m", "Mean vertical distance magnitude (m)"),
            ("MeanHorizontalSpeed_mps", "Mean horizontal speed (m/s)"),
            ("MeanAbsVerticalSpeed_mps", "Mean vertical speed magnitude (m/s)"),
        ],
    ),
    (
        "05_step_control_safety",
        "Control And Safety From Step Telemetry",
        [
            ("MeanThrottle01", "Mean throttle (0..1)"),
            ("MeanGimbalDeflectionDeg", "Mean gimbal deflection (deg)"),
            ("MeanFinDeflectionDeg", "Mean fin deflection (deg)"),
            ("MeanLoadFactorG", "Mean load factor (g)"),
            ("MeanDynamicPressurePa", "Mean dynamic pressure (Pa)"),
        ],
    ),
]


STEP_DERIVED_EXPLANATIONS = {
    "CycleCompletions": "Count of successful hover-track cycles in the episode: travel to the moved pad, stabilize, and hold long enough to capture the target.",
    "CycleRate_per_min": "Cycle completions normalized by episode duration. This stays useful as episodes get longer or targets move farther away.",
    "MeanStepReward": "Average reward per physics step for the logged area.",
    "EpisodeDuration_s": "Episode length converted to seconds from the Unity fixed timestep.",
    "MaxStableHoverTime_s": "Longest continuous hover-ready hold in the episode.",
    "TargetJumpDistance_m": "Typical horizontal distance to the target after a pad move.",
    "MovingHorizontalClosureSpeed_mps": "Average horizontal speed toward the pad while still outside the hover radius.",
    "MovingDirectionAccuracy01": "Average movement direction accuracy while outside the hover radius; 1 means moving directly toward the pad.",
    "MovingNormalizedTravelRate_1ps": "Moving-phase closure speed divided by target jump distance, so long pad jumps remain comparable to short ones.",
    "FinalTravelProgress01": "Travel progress at the end of the episode; 1 means the rocket reached the current target radius.",
    "HoverReadyFraction01": "Fraction of logged steps that satisfy the hover-ready success condition.",
    "HoverStabilizationQuality01": "Average stabilization quality while inside the hover radius.",
    "HoverHorizontalSpeed_mps": "Average horizontal speed while inside the hover radius.",
    "HoverAbsVerticalSpeed_mps": "Average vertical speed magnitude while inside the hover radius.",
    "HoverTiltDeg": "Average tilt while inside the hover radius.",
    "MeanHorizontalDistanceToGoal_m": "Episode-average horizontal distance from the target pad.",
    "FinalHorizontalDistanceToGoal_m": "Horizontal distance from the pad at the final logged step of the episode.",
    "MeanAbsVerticalDistanceToGoal_m": "Episode-average absolute height error.",
    "MeanHorizontalSpeed_mps": "Episode-average horizontal speed.",
    "MeanAbsVerticalSpeed_mps": "Episode-average vertical speed magnitude.",
    "MeanThrottle01": "Episode-average throttle demand.",
    "MeanGimbalDeflectionDeg": "Episode-average engine gimbal effort.",
    "MeanFinDeflectionDeg": "Episode-average fin effort.",
    "MeanLoadFactorG": "Episode-average load factor.",
    "MeanDynamicPressurePa": "Episode-average dynamic pressure.",
}


GRAPH_DESCRIPTIONS = {
    "00_performance_metrics": (
        "Performance metrics across episodes. The line is the mean across all training areas for that episode; "
        "the shaded band is the standard deviation across those areas. Reward shows the learning signal, episode "
        "length shows survival/task duration, and duration converts length to seconds."
    ),
    "00_summary_dashboard": (
        "A compact episode overview. It shows the main training health signals in one place using cross-area "
        "episode means and standard-deviation envelopes."
    ),
    "01_goal_tracking": (
        "Position tracking quality in physical units. Goal distance is full 3D position error in meters, "
        "horizontal error is XZ-plane target miss distance in meters, vertical error is height mismatch in meters, "
        "and altitude is the rocket height in meters."
    ),
    "02_navigation_bearings": (
        "Directional navigation behavior. Target bearing says where the pad is, velocity bearing says where the "
        "rocket is moving, velocity-target error says whether motion is aimed at the pad, goal alignment is a "
        "dot product where 1 means moving directly toward the target, and gimbal bearing shows the steering direction."
    ),
    "03_attitude_rotation": (
        "Rotational stability. Tilt and angular rates show whether the rocket remains upright and whether pitch, "
        "yaw, or roll is the dominant instability."
    ),
    "04_velocity": (
        "Velocity control in meters per second. These graphs separate total 3D, horizontal, vertical, "
        "goal-closing, and air-relative motion."
    ),
    "05_control_fuel": (
        "Control effort and fuel use. Throttle shows lift demand, gimbal and fin effort show steering demand, "
        "and fuel curves show efficiency and episode duration effects."
    ),
    "06_aero_loads": (
        "Safety and stress metrics. These reveal whether the learned policy is physically gentle or creating "
        "large aerodynamic, thermal, or structural loads."
    ),
    "07_reward": (
        "The scalar learning signal received at each timestep, aggregated per episode across training areas."
    ),
    "08_hover_track_curriculum": (
        "Hover-track curriculum diagnostics. These expose the task difficulty and normalized progress signals, "
        "so a larger pad jump does not make the run look worse just because the target moved farther away."
    ),
    "01_step_hover_track_outcomes": (
        "Step telemetry summarized by episode. These line charts turn noisy per-timestep samples into readable "
        "outcome signals: completed travel+stabilize cycles, cycle rate, reward, duration, and stable hover time."
    ),
    "02_step_hover_track_travel": (
        "Travel quality derived from step telemetry. These metrics focus on the moving phase: target jump distance, "
        "closure speed, direction accuracy, normalized travel rate, and final travel progress."
    ),
    "03_step_hover_track_stabilization": (
        "Stabilization quality derived from step telemetry. These metrics focus on the hover phase: hover-ready "
        "fraction, stabilization quality, horizontal/vertical calm, and tilt."
    ),
    "04_step_position_motion": (
        "Position and motion summarized per episode from the step CSV. These are plain physical quantities in "
        "meters and meters per second, useful for seeing whether the policy is actually getting calmer."
    ),
    "05_step_control_safety": (
        "Control and safety summarized per episode from the step CSV. These show whether improvement is coming "
        "from smoother control or from high effort/load behavior."
    ),
    "06_step_episode_progress_profiles": (
        "Within-episode line profiles. Each episode is normalized from 0% to 100%, then the plot shows the typical "
        "trajectory using median, mean, and middle-50% envelopes."
    ),
}


METRIC_EXPLANATIONS = {
    "Reward_Step": "Higher reward usually means the policy is satisfying more of the scenario objective with fewer penalties.",
    "Goal_Distance3D_m": "A falling goal distance means the agent is getting closer to the target in full 3D space.",
    "Goal_PlanarDistance_m": "Horizontal error in meters isolates XZ-plane tracking, which is often the hard part after altitude stabilizes.",
    "Goal_AbsVerticalError_m": "Absolute vertical error in meters shows whether the policy learned the target height instead of only moving sideways.",
    "Track_PhaseHover01": "Hover-tracking phase flag: 0 means travel toward the pad, 1 means settle/hover near the pad.",
    "Track_HoverReady01": "Hover-ready flag: 1 means the rocket is inside the target radius, slow, upright, and altitude-stable enough to count toward a pad move.",
    "Track_StableTime_s": "Seconds continuously spent in the hover-ready state before the target is moved.",
    "Track_TargetReached01": "Instantaneous success pulse when stable hover has been held long enough to move the target pad.",
    "Track_SettleRadius_m": "Configured horizontal distance threshold that switches hover tracking from moving phase to hover phase.",
    "Track_CurriculumProgress01": "Automatic hover-track curriculum progress: 0 is easiest, 1 is hardest.",
    "Track_SegmentStartDistance_m": "Horizontal distance to the newly moved pad at the start of the current target segment.",
    "Track_SegmentElapsed_s": "Seconds since the current target segment began.",
    "Track_TravelProgress01": "Relative travel progress from the current segment start distance toward the pad.",
    "Track_TravelProgressRate_1ps": "Horizontal closure rate divided by segment start distance; useful when pad jumps get longer.",
    "Track_DirectionEfficiency01": "How well horizontal velocity points toward the pad: 0 away, 0.5 sideways/stationary, 1 directly toward.",
    "Track_SettleQuality01": "Relative stabilization score combining position, speed, tilt, vertical speed, and angular calm.",
    "State_Altitude_m": "Altitude reveals whether the vehicle converged to the intended hover or landing height.",
    "Nav_TargetBearingDeg": "The compass-like direction from the rocket to the target pad in the XZ plane.",
    "Nav_VelocityBearingDeg": "The direction the rocket is actually moving horizontally.",
    "Nav_VelocityTargetErrorDeg": "Signed angular difference between movement direction and target direction; near zero means motion points toward the pad.",
    "Nav_GoalAlignment": "Dot product between horizontal velocity direction and target direction; 1 is toward target, -1 is away.",
    "Nav_GimbalBearingDeg": "Mean engine gimbal direction in the XZ plane.",
    "Nav_GimbalTargetErrorDeg": "Signed angular difference between gimbal direction and target direction.",
    "Att_TiltDeg": "Lower tilt means the vehicle is staying more upright and wasting less thrust sideways.",
    "Att_AngularRateDegS": "Lower angular rate means fewer uncontrolled rotations and a calmer attitude controller.",
    "Att_TiltRateDegS": "Lower tilt rate means the rocket is not rapidly tipping back and forth.",
    "Att_PitchRateDegS": "Signed pitch rate helps diagnose front/back tipping corrections.",
    "Att_YawRateDegS": "Signed yaw rate helps diagnose heading rotation.",
    "Att_RollRateDegS": "Signed roll rate helps diagnose spin about the rocket body.",
    "Aero_AoaDeg": "Angle of attack compares vehicle direction with air-relative velocity.",
    "Vel_Speed3D_mps": "Lower speed near the end usually means the policy is settling instead of overshooting.",
    "Vel_PlanarSpeed_mps": "Horizontal speed in m/s shows how aggressively the vehicle is moving across the pad or target plane.",
    "Vel_VerticalSpeed_mps": "Vertical speed near zero means height is stabilized.",
    "Vel_GoalClosureRate_mps": "Positive values mean the rocket is moving toward the target; near zero means it has mostly settled.",
    "Vel_HorizontalClosureRate_mps": "Positive horizontal closure rate means the rocket is moving toward the pad in the XZ plane.",
    "Vel_AirRelativeSpeed_mps": "Lower air-relative speed reduces aerodynamic pressure and structural loads.",
    "Ctrl_ThrottleMean": "Throttle mean shows the average lift demand from the engine.",
    "Ctrl_ThrottleMax": "Throttle max reveals whether the policy still needs extreme thrust bursts.",
    "Ctrl_GimbalMeanAbsDeg": "Gimbal effort shows how much the engine is being steered to correct attitude or position.",
    "Ctrl_FinMeanAbsDeg": "Fin effort shows aerodynamic steering demand.",
    "Fuel_Fraction": "Higher fuel fraction at comparable task length means better efficiency.",
    "Fuel_UsedKg": "Fuel used increases with burn time and throttle demand.",
    "Load_GForce": "Load factor in g; hover normally sits near 1 g because thrust must counter gravity.",
    "Load_AngularAccelDegS2": "Lower angular acceleration means fewer sharp rotational corrections.",
    "Load_DynPressurePa": "Dynamic pressure falls when air-relative speed falls, reducing aerodynamic stress.",
    "Thermal_HeatFluxWm2": "Near-zero heat flux usually means the vehicle never reached speeds where aerodynamic heating matters.",
    "Stress_BendingPa": "Bending stress reflects side loads and tilt/velocity interactions.",
    "Stress_AxialPa": "Axial stress mostly follows vertical thrust and acceleration loads.",
}


ANALYSIS_METRICS = [
    ("Reward_Step", "Reward", "higher"),
    ("Goal_Distance3D_m", "3D goal error (m)", "lower"),
    ("Goal_PlanarDistance_m", "Horizontal error (m)", "lower"),
    ("Goal_AbsVerticalError_m", "Absolute vertical error (m)", "lower"),
    ("Nav_GoalAlignment", "Goal alignment (-1..1)", "higher"),
    ("Nav_VelocityTargetErrorDeg", "Velocity-target bearing error", "abs_lower"),
    ("Att_TiltDeg", "Tilt", "lower"),
    ("Att_AngularRateDegS", "Angular rate", "lower"),
    ("Vel_Speed3D_mps", "3D speed (m/s)", "lower"),
    ("Vel_VerticalSpeed_mps", "Vertical speed magnitude (m/s)", "abs_lower"),
    ("Vel_GoalClosureRate_mps", "Goal closure rate (m/s)", "context"),
    ("Vel_HorizontalClosureRate_mps", "Horizontal closure rate (m/s)", "higher"),
    ("Track_TravelProgressRate_1ps", "Distance-normalized travel rate (1/s)", "higher"),
    ("Track_DirectionEfficiency01", "Direction efficiency (0..1)", "higher"),
    ("Track_SettleQuality01", "Relative settle quality (0..1)", "higher"),
    ("Vel_AirRelativeSpeed_mps", "Air-relative speed (m/s)", "lower"),
    ("Ctrl_ThrottleMean", "Mean throttle (0..1)", "context"),
    ("Ctrl_GimbalMeanAbsDeg", "Gimbal effort", "context"),
    ("Ctrl_FinMeanAbsDeg", "Fin effort", "context"),
    ("Fuel_Fraction", "Fuel remaining fraction (0..1)", "higher"),
    ("Fuel_UsedKg", "Fuel used (kg)", "context"),
    ("Load_GForce", "Load factor (g)", "near_one"),
    ("Load_DynPressurePa", "Dynamic pressure (Pa)", "lower"),
    ("Thermal_HeatFluxWm2", "Heat flux (W/m^2)", "lower"),
    ("Stress_BendingPa", "Bending stress (Pa)", "lower"),
    ("Stress_AxialPa", "Axial stress (Pa)", "lower"),
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Plot RocketSim telemetry CSVs.")
    parser.add_argument("csv", nargs="?", help="Path to telemetry steps or episodes CSV.")
    parser.add_argument("--output-dir", default="", help="Directory where PNG plots should be saved.")
    parser.add_argument("--smooth", type=int, default=STEP_SMOOTH_WINDOW, help="Step timeline smoothing window. Use 1 for no smoothing.")
    parser.add_argument("--fixed-dt", type=float, default=FIXED_DELTA_TIME, help="Unity fixed timestep used for episode time plots.")
    parser.add_argument("--max-step-rows", type=int, default=MAX_STEP_ROWS, help="Maximum sampled rows for step episode-progress profile plots.")
    parser.add_argument("--include-bootstrap", action="store_true", help="Keep obvious bootstrap/reset rows in plots.")
    return parser.parse_args()


def main() -> None:
    args = parse_args()

    csv_path = Path(args.csv or TELEMETRY_CSV).expanduser()
    if not csv_path.exists():
        raise FileNotFoundError(f"Telemetry CSV not found: {csv_path}")

    out_dir = Path(args.output_dir or OUTPUT_DIR or csv_path.with_suffix("").as_posix() + "_plots")
    out_dir.mkdir(parents=True, exist_ok=True)

    df = pd.read_csv(csv_path)
    mode = detect_mode(df, csv_path)
    df = prepare_dataframe(df, mode)
    if mode == "episodes" and not args.include_bootstrap:
        df = filter_bootstrap_episodes(df)
    elif mode == "steps" and not args.include_bootstrap:
        df = filter_bootstrap_steps(df)
    x_col = choose_x_column(df, mode)

    saved: list[Path] = []

    if mode == "episodes":
        performance = plot_performance_metrics(df, args.fixed_dt, out_dir / "00_performance_metrics.png")
        if performance is not None:
            saved.append(performance)

        summary = plot_summary_dashboard(df, mode, x_col, out_dir / "00_summary_dashboard.png")
        if summary is not None:
            saved.insert(0, summary)

        for filename, title, metrics in EPISODE_GROUPS:
            output = plot_episode_group(df, x_col, title, metrics, out_dir / f"{filename}.png")
            if output is not None:
                saved.append(output)
    else:
        step_summary = build_step_episode_summary(df, args.fixed_dt)
        saved.extend(plot_step_episode_groups(step_summary, out_dir))

        step_df = sample_steps(df, args.max_step_rows)
        timeline = plot_step_timeseries(step_df, choose_x_column(step_df, mode), args.smooth, out_dir / "06_step_episode_progress_profiles.png")
        if timeline is not None:
            saved.append(timeline)

    report = write_report(df, mode, x_col, csv_path, out_dir, saved)

    print(f"Read {mode} telemetry: {csv_path}")
    print(f"Saved {len(saved)} plot(s) to: {out_dir}")
    for path in saved:
        print(f" - {path.name}")
    print(f" - {report.name}")


def detect_mode(df: pd.DataFrame, path: Path) -> str:
    name = path.name.lower()
    if "episodes" in name:
        return "episodes"
    if "steps" in name:
        return "steps"
    if any(col.endswith("_Mean") for col in df.columns):
        return "episodes"
    return "steps"


def prepare_dataframe(df: pd.DataFrame, mode: str) -> pd.DataFrame:
    df = df.copy()
    for col in df.columns:
        if col == "WallTime":
            continue
        converted = pd.to_numeric(df[col], errors="coerce")
        if converted.notna().any():
            df[col] = converted

    normalize_angle_columns(df)

    if mode == "steps" and {"Episode", "Step"}.issubset(df.columns):
        df["GlobalStep"] = range(len(df))

    return df


def normalize_angle_columns(df: pd.DataFrame) -> None:
    for col in df.columns:
        if any(token in col for token in ANGLE_METRIC_TOKENS):
            values = pd.to_numeric(df[col], errors="coerce")
            if values.notna().any():
                df[col] = ((values + 180.0) % 360.0) - 180.0


def filter_bootstrap_episodes(df: pd.DataFrame) -> pd.DataFrame:
    if "Episode" not in df.columns or df.empty:
        return df

    first_episode = pd.to_numeric(df["Episode"], errors="coerce").min()
    first = df[pd.to_numeric(df["Episode"], errors="coerce") == first_episode]
    if first.empty:
        return df

    bad_signals = 0
    reward_col = metric_col(df, "Reward_Step", "episodes")
    g_col = metric_col(df, "Load_GForce", "episodes")
    fuel_fraction_col = metric_col(df, "Fuel_Fraction", "episodes")
    fuel_used_col = metric_col(df, "Fuel_UsedKg", "episodes")

    if reward_col and pd.to_numeric(first[reward_col], errors="coerce").mean() < -5.0:
        bad_signals += 1
    if g_col and pd.to_numeric(first[g_col], errors="coerce").mean() < 0.25:
        bad_signals += 1
    if fuel_fraction_col and pd.to_numeric(first[fuel_fraction_col], errors="coerce").mean() < 0.1:
        bad_signals += 1
    if fuel_used_col:
        first_fuel = pd.to_numeric(first[fuel_used_col], errors="coerce").mean()
        later_fuel = pd.to_numeric(df[df["Episode"] != first_episode][fuel_used_col], errors="coerce")
        if later_fuel.notna().any() and first_fuel > later_fuel.quantile(0.95) * 1.25:
            bad_signals += 1
    if "StepCount" in df.columns and pd.to_numeric(first["StepCount"], errors="coerce").mean() <= 2:
        bad_signals += 1

    if bad_signals >= 2:
        print(f"Dropped bootstrap episode {first_episode:g} from plots ({bad_signals} reset-like signals).")
        return df[pd.to_numeric(df["Episode"], errors="coerce") != first_episode].copy()

    return df


def filter_bootstrap_steps(df: pd.DataFrame) -> pd.DataFrame:
    if "Step" not in df.columns:
        return df

    # First logged physics samples can contain reset-derived acceleration and
    # uninformative zero actions. Keeping them pollutes step summaries.
    step = pd.to_numeric(df["Step"], errors="coerce")
    filtered = df[step > 1].copy()
    return filtered if not filtered.empty else df


def choose_x_column(df: pd.DataFrame, mode: str) -> str:
    if mode == "episodes" and "Episode" in df.columns:
        return "Episode"
    if mode == "steps" and "GlobalStep" in df.columns:
        return "GlobalStep"
    if "Step" in df.columns:
        return "Step"
    return df.columns[0]


def metric_col(df: pd.DataFrame, metric: str, mode: str) -> str | None:
    candidates = [metric, *TELEMETRY_ALIASES.get(metric, ())]
    for candidate in candidates:
        if candidate in df.columns:
            return candidate
        mean_col = f"{candidate}_Mean"
        if mode == "episodes" and mean_col in df.columns:
            return mean_col
    return None


def sample_steps(df: pd.DataFrame, max_rows: int) -> pd.DataFrame:
    if max_rows <= 0 or len(df) <= max_rows:
        return df

    stride = math.ceil(len(df) / max_rows)
    print(f"Sampled step CSV with stride {stride}; plotting {math.ceil(len(df) / stride):,} of {len(df):,} rows.")
    return df.iloc[::stride].copy()


def smooth(series: pd.Series, window: int) -> pd.Series:
    if window <= 1 or len(series) < window:
        return series
    return series.rolling(window=window, min_periods=max(1, window // 5)).mean()


def area_episode_stats(df: pd.DataFrame, x_col: str, y_col: str) -> pd.DataFrame:
    tmp = df[[x_col, y_col]].copy()
    tmp[x_col] = pd.to_numeric(tmp[x_col], errors="coerce")
    tmp[y_col] = pd.to_numeric(tmp[y_col], errors="coerce")
    tmp = tmp.dropna()
    if tmp.empty:
        return pd.DataFrame(columns=["x", "mean", "std", "count"])

    if is_angle_metric(y_col):
        rows = []
        for x_value, group in tmp.groupby(x_col, sort=True):
            mean, std = circular_mean_std_deg(group[y_col])
            rows.append({"x": x_value, "mean": mean, "std": std, "count": len(group)})
        return pd.DataFrame(rows).sort_values("x")

    grouped = tmp.groupby(x_col, as_index=False).agg(
        mean=(y_col, "mean"),
        std=(y_col, "std"),
        count=(y_col, "count"),
    )
    grouped = grouped.rename(columns={x_col: "x"})
    grouped["std"] = grouped["std"].fillna(0)
    return grouped.sort_values("x")


def is_angle_metric(column: str | None) -> bool:
    return bool(column) and any(token in column for token in ANGLE_METRIC_TOKENS)


def circular_mean_std_deg(values: pd.Series) -> tuple[float, float]:
    numeric = pd.to_numeric(values, errors="coerce").dropna()
    if numeric.empty:
        return float("nan"), 0.0

    radians = np.deg2rad(numeric.to_numpy(dtype=float))
    sin_mean = np.sin(radians).mean()
    cos_mean = np.cos(radians).mean()
    mean = np.rad2deg(np.arctan2(sin_mean, cos_mean))

    resultant_length = np.hypot(sin_mean, cos_mean)
    resultant_length = max(float(resultant_length), 1e-9)
    std = np.rad2deg(np.sqrt(max(0.0, -2.0 * np.log(resultant_length))))
    return float(mean), float(min(std, 180.0))


def plot_mean_std(ax, stats: pd.DataFrame, label: str) -> None:
    x = stats["x"]
    mean = stats["mean"]
    std = stats["std"]
    ax.plot(x, mean, color="#1f77b4", linewidth=2.0, label=f"{label} area mean")
    ax.fill_between(x, mean - std, mean + std, color="#1f77b4", alpha=0.18, linewidth=0, label="+/- 1 area std")


def plot_episode_group(
    df: pd.DataFrame,
    x_col: str,
    title: str,
    metrics: list[tuple[str, str]],
    output_path: Path,
) -> Path | None:
    available = [(metric_col(df, metric, "episodes"), metric, label) for metric, label in metrics]
    available = [(col, metric, label) for col, metric, label in available if col is not None]
    if not available:
        return None

    fig, axes = plt.subplots(len(available), 1, figsize=(13, 3.25 * len(available)), sharex=True)
    if len(available) == 1:
        axes = [axes]

    for ax, (col, _, label) in zip(axes, available):
        stats = area_episode_stats(df, x_col, col)
        if stats.empty:
            continue
        plot_mean_std(ax, stats, label)
        ax.set_ylabel(label)
        ax.grid(True, alpha=0.25)
        ax.legend(loc="best", fontsize=8)
        ax.set_xlabel(EPISODE_X_LABEL)
        add_axis_caption(ax, metric_caption(base_metric_name(col), label))

    fig.suptitle(f"{title} (episodes, mean/std across areas)", fontsize=15)
    fig.tight_layout(rect=(0, 0, 1, 0.97))
    fig.savefig(output_path, dpi=160)
    plt.close(fig)
    return output_path


def plot_summary_dashboard(
    df: pd.DataFrame,
    mode: str,
    x_col: str,
    output_path: Path,
) -> Path | None:
    summary_metrics = [
        ("Reward_Step", "Step reward"),
        ("Goal_Distance3D_m", "3D goal error (m)"),
        ("Goal_PlanarDistance_m", "Horizontal error (m)"),
        ("Nav_GoalAlignment", "Goal alignment (-1..1)"),
        ("Att_TiltDeg", "Tilt (deg)"),
        ("Vel_Speed3D_mps", "3D speed (m/s)"),
        ("Ctrl_ThrottleMean", "Mean throttle (0..1)"),
        ("Load_GForce", "Load factor (g)"),
    ]
    available = [(metric_col(df, metric, mode), label) for metric, label in summary_metrics]
    available = [(col, label) for col, label in available if col is not None]
    if not available:
        return None

    cols = 2
    rows = math.ceil(len(available) / cols)
    fig, axes = plt.subplots(rows, cols, figsize=(14, 3.85 * rows), sharex=True)
    axes = axes.flatten()

    for ax, (col, label) in zip(axes, available):
        if mode == "episodes":
            stats = area_episode_stats(df, x_col, col)
            if not stats.empty:
                plot_mean_std(ax, stats, label)
        else:
            x = df[x_col]
            y = pd.to_numeric(df[col], errors="coerce")
            ax.plot(x, smooth(y, STEP_SMOOTH_WINDOW), linewidth=1.6, color="#1f77b4")
        ax.set_title(label)
        ax.set_xlabel(x_axis_label(mode, x_col))
        add_axis_caption(ax, metric_caption(base_metric_name(col), label))
        ax.grid(True, alpha=0.25)

    for ax in axes[len(available):]:
        ax.axis("off")

    fig.suptitle(f"RocketSim Telemetry Summary ({mode})", fontsize=16)
    fig.tight_layout(rect=(0, 0, 1, 0.96))
    fig.savefig(output_path, dpi=160)
    plt.close(fig)
    return output_path


def plot_performance_metrics(
    df: pd.DataFrame,
    fixed_dt: float,
    output_path: Path,
) -> Path | None:
    if "Episode" not in df.columns:
        return None

    working = df.copy()
    metrics = []
    reward_col = metric_col(working, "Reward_Step", "episodes")
    if reward_col is not None:
        metrics.append((reward_col, "Average step reward"))

    if "StepCount" in working.columns:
        metrics.append(("StepCount", "Episode length (steps)"))
        working["EpisodeDuration_s"] = pd.to_numeric(working["StepCount"], errors="coerce") * fixed_dt
        metrics.append(("EpisodeDuration_s", "Episode duration (s)"))

    if not metrics:
        return None

    fig, axes = plt.subplots(len(metrics), 1, figsize=(13, 3.35 * len(metrics)), sharex=True)
    if len(metrics) == 1:
        axes = [axes]

    for ax, (col, label) in zip(axes, metrics):
        stats = area_episode_stats(working, "Episode", col)
        if stats.empty:
            continue
        plot_mean_std(ax, stats, label)
        ax.set_ylabel(label)
        ax.set_xlabel(EPISODE_X_LABEL)
        add_axis_caption(ax, performance_caption(col, label))
        ax.grid(True, alpha=0.25)
        ax.legend(loc="best", fontsize=8)

    fig.suptitle("Agent Performance Metrics (mean/std across training areas)", fontsize=15)
    fig.tight_layout(rect=(0, 0, 1, 0.96))
    fig.savefig(output_path, dpi=160)
    plt.close(fig)
    return output_path


def build_step_episode_summary(df: pd.DataFrame, fixed_dt: float) -> pd.DataFrame:
    if "Episode" not in df.columns or df.empty:
        return pd.DataFrame()

    work = df.copy()
    work["Episode"] = pd.to_numeric(work["Episode"], errors="coerce")
    if "Step" in work.columns:
        work["Step"] = pd.to_numeric(work["Step"], errors="coerce")
    else:
        work["Step"] = work.groupby("Episode").cumcount()
    work["_row_order"] = np.arange(len(work))
    work = work.dropna(subset=["Episode"]).copy()
    if work.empty:
        return pd.DataFrame()

    episodes = np.sort(work["Episode"].dropna().unique())
    summary = pd.DataFrame({"Episode": episodes}).set_index("Episode")

    step_span = work.groupby("Episode")["Step"].agg(lambda values: values.max() - values.min() + 1)
    row_count = work.groupby("Episode")["Step"].count()
    summary["StepCount"] = step_span.reindex(summary.index).fillna(row_count).clip(lower=1)
    summary["EpisodeDuration_s"] = summary["StepCount"] * fixed_dt

    def series_for(metric: str, absolute: bool = False) -> pd.Series | None:
        col = metric_col(work, metric, "steps")
        if col is None:
            return None
        values = pd.to_numeric(work[col], errors="coerce")
        return values.abs() if absolute else values

    phase = series_for("Track_PhaseHover01")
    moving_mask = phase < 0.5 if phase is not None else pd.Series(True, index=work.index)
    hover_mask = phase >= 0.5 if phase is not None else pd.Series(True, index=work.index)

    def add_agg(
        output_name: str,
        metric: str,
        agg: str,
        *,
        absolute: bool = False,
        mask: pd.Series | None = None,
    ) -> None:
        values = series_for(metric, absolute=absolute)
        if values is None:
            return
        tmp = pd.DataFrame({"Episode": work["Episode"], "value": values})
        if mask is not None:
            tmp = tmp[mask]
        tmp = tmp.dropna()
        if tmp.empty:
            return
        summary[output_name] = tmp.groupby("Episode")["value"].agg(agg).reindex(summary.index)

    def add_final(output_name: str, metric: str, *, absolute: bool = False) -> None:
        values = series_for(metric, absolute=absolute)
        if values is None:
            return
        tmp = pd.DataFrame({
            "Episode": work["Episode"],
            "Step": work["Step"],
            "RowOrder": work["_row_order"],
            "value": values,
        }).dropna()
        if tmp.empty:
            return
        last = tmp.sort_values(["Episode", "Step", "RowOrder"]).groupby("Episode")["value"].last()
        summary[output_name] = last.reindex(summary.index)

    add_agg("MeanStepReward", "Reward_Step", "mean")
    add_agg("CycleCompletions", "Track_TargetReached01", "sum")
    add_agg("MaxStableHoverTime_s", "Track_StableTime_s", "max")

    add_agg("TargetJumpDistance_m", "Track_SegmentStartDistance_m", "median")
    add_agg("MovingHorizontalClosureSpeed_mps", "Vel_HorizontalClosureRate_mps", "mean", mask=moving_mask)
    add_agg("MovingDirectionAccuracy01", "Track_DirectionEfficiency01", "mean", mask=moving_mask)
    add_agg("MovingNormalizedTravelRate_1ps", "Track_TravelProgressRate_1ps", "mean", mask=moving_mask)
    add_final("FinalTravelProgress01", "Track_TravelProgress01")

    add_agg("HoverReadyFraction01", "Track_HoverReady01", "mean")
    add_agg("HoverStabilizationQuality01", "Track_SettleQuality01", "mean", mask=hover_mask)
    add_agg("HoverHorizontalSpeed_mps", "Vel_PlanarSpeed_mps", "mean", mask=hover_mask)
    add_agg("HoverAbsVerticalSpeed_mps", "Vel_VerticalSpeed_mps", "mean", absolute=True, mask=hover_mask)
    add_agg("HoverTiltDeg", "Att_TiltDeg", "mean", mask=hover_mask)

    add_agg("MeanHorizontalDistanceToGoal_m", "Goal_PlanarDistance_m", "mean")
    add_final("FinalHorizontalDistanceToGoal_m", "Goal_PlanarDistance_m")
    add_agg("MeanAbsVerticalDistanceToGoal_m", "Goal_AbsVerticalError_m", "mean")
    add_agg("MeanHorizontalSpeed_mps", "Vel_PlanarSpeed_mps", "mean")
    add_agg("MeanAbsVerticalSpeed_mps", "Vel_VerticalSpeed_mps", "mean", absolute=True)

    add_agg("MeanThrottle01", "Ctrl_ThrottleMean", "mean")
    add_agg("MeanGimbalDeflectionDeg", "Ctrl_GimbalMeanAbsDeg", "mean")
    add_agg("MeanFinDeflectionDeg", "Ctrl_FinMeanAbsDeg", "mean")
    add_agg("MeanLoadFactorG", "Load_GForce", "mean")
    add_agg("MeanDynamicPressurePa", "Load_DynPressurePa", "mean")

    if "CycleCompletions" in summary.columns:
        summary["CycleCompletions"] = summary["CycleCompletions"].fillna(0.0)
        duration = summary["EpisodeDuration_s"].replace(0.0, np.nan)
        summary["CycleRate_per_min"] = summary["CycleCompletions"] / duration * 60.0

    return summary.reset_index().sort_values("Episode")


def plot_step_episode_groups(summary: pd.DataFrame, out_dir: Path) -> list[Path]:
    saved = []
    if summary.empty:
        return saved

    for filename, title, metrics in STEP_EPISODE_GROUPS:
        path = plot_step_episode_group(summary, title, metrics, out_dir / f"{filename}.png")
        if path is not None:
            saved.append(path)
    return saved


def plot_step_episode_group(
    summary: pd.DataFrame,
    title: str,
    metrics: list[tuple[str, str]],
    output_path: Path,
) -> Path | None:
    available = [
        (metric, label)
        for metric, label in metrics
        if metric in summary.columns and pd.to_numeric(summary[metric], errors="coerce").notna().any()
    ]
    if not available or "Episode" not in summary.columns:
        return None

    fig, axes = plt.subplots(len(available), 1, figsize=(13, 3.1 * len(available)), sharex=True)
    if len(available) == 1:
        axes = [axes]

    for ax, (metric, label) in zip(axes, available):
        tmp = summary[["Episode", metric]].copy()
        tmp["Episode"] = pd.to_numeric(tmp["Episode"], errors="coerce")
        tmp[metric] = pd.to_numeric(tmp[metric], errors="coerce")
        tmp = tmp.dropna().sort_values("Episode")
        if tmp.empty:
            ax.axis("off")
            continue

        x = tmp["Episode"]
        y = tmp[metric]
        ax.plot(x, y, color="#9aa0a6", alpha=0.35, linewidth=0.9, label="episode value")
        window = step_episode_rolling_window(len(tmp))
        if window > 1:
            trend = y.rolling(window=window, min_periods=max(1, window // 4)).mean()
            ax.plot(x, trend, color="#1f77b4", linewidth=2.1, label=f"{window}-episode trend")
        else:
            ax.plot(x, y, color="#1f77b4", linewidth=1.8, label="trend")

        ax.set_ylabel(label)
        ax.set_xlabel(EPISODE_X_LABEL)
        ax.grid(True, alpha=0.25)
        ax.legend(loc="best", fontsize=8)
        add_axis_caption(ax, step_episode_caption(metric, label))

    fig.suptitle(f"{title} (derived from steps CSV)", fontsize=15)
    fig.tight_layout(rect=(0, 0, 1, 0.97))
    fig.savefig(output_path, dpi=160)
    plt.close(fig)
    return output_path


def step_episode_rolling_window(length: int) -> int:
    if length < 6:
        return 1
    return max(3, min(25, length // 12 if length >= 36 else 5))


def plot_step_timeseries(
    df: pd.DataFrame,
    x_col: str,
    smooth_window: int,
    output_path: Path,
) -> Path | None:
    available = [(metric_col(df, metric, "steps"), label) for metric, label in STEP_TIMESERIES]
    available = [(col, label) for col, label in available if col is not None]
    if not available:
        return None

    fig, axes = plt.subplots(len(available), 1, figsize=(13, 3.05 * len(available)), sharex=True)
    if len(available) == 1:
        axes = [axes]

    for ax, (col, label) in zip(axes, available):
        profile = episode_progress_profile(df, col)
        if profile.empty:
            y = pd.to_numeric(df[col], errors="coerce")
            x = df[x_col]
            ax.plot(x, y, color="#9aa0a6", alpha=0.22, linewidth=0.7, label="raw")
            ax.plot(x, smooth(y, smooth_window), color="#1f77b4", linewidth=1.6, label="smoothed")
            ax.set_xlabel(x_axis_label("steps", x_col))
        else:
            ax.plot(profile["progress"], profile["median"], color="#1f77b4", linewidth=1.8, label="median")
            ax.fill_between(
                profile["progress"],
                profile["p25"],
                profile["p75"],
                color="#1f77b4",
                alpha=0.18,
                linewidth=0,
                label="middle 50%",
            )
            ax.plot(profile["progress"], profile["mean"], color="#d95f02", linewidth=1.2, alpha=0.75, label="mean")
            ax.set_xlabel(STEP_PROGRESS_X_LABEL)
        ax.set_ylabel(label)
        add_axis_caption(ax, progress_caption(base_metric_name(col), label))
        ax.grid(True, alpha=0.25)
        ax.legend(loc="best", fontsize=8)

    fig.suptitle("Within-Episode Step Profiles", fontsize=15)
    fig.tight_layout(rect=(0, 0, 1, 0.97))
    fig.savefig(output_path, dpi=160)
    plt.close(fig)
    return output_path


def episode_progress_profile(df: pd.DataFrame, metric_col_name: str) -> pd.DataFrame:
    if not {"Episode", "Step"}.issubset(df.columns):
        return pd.DataFrame(columns=["progress", "mean", "median", "p25", "p75"])

    work = df[["Episode", "Step", metric_col_name]].copy()
    work["Episode"] = pd.to_numeric(work["Episode"], errors="coerce")
    work["Step"] = pd.to_numeric(work["Step"], errors="coerce")
    work[metric_col_name] = pd.to_numeric(work[metric_col_name], errors="coerce")
    work = work.dropna()
    if work.empty or work["Episode"].nunique() < 2:
        return pd.DataFrame(columns=["progress", "mean", "median", "p25", "p75"])

    max_step = work.groupby("Episode")["Step"].transform("max").clip(lower=1)
    work["Progress"] = (work["Step"] / max_step * 100.0).clip(0.0, 100.0)
    work["_bin"] = np.floor(work["Progress"] / (100.0 / STEP_PROFILE_BINS)).astype(int)
    work["_bin"] = work["_bin"].clip(0, STEP_PROFILE_BINS - 1)

    grouped = work.groupby("_bin", observed=True).agg(
        progress=("Progress", "mean"),
        mean=(metric_col_name, "mean"),
        median=(metric_col_name, "median"),
        p25=(metric_col_name, lambda values: values.quantile(0.25)),
        p75=(metric_col_name, lambda values: values.quantile(0.75)),
        count=(metric_col_name, "count"),
    )
    grouped = grouped[grouped["count"] >= 5].reset_index(drop=True)
    return grouped.sort_values("progress")


def write_report(
    df: pd.DataFrame,
    mode: str,
    x_col: str,
    csv_path: Path,
    out_dir: Path,
    saved_plots: list[Path],
) -> Path:
    report_path = out_dir / "telemetry_report.md"
    saved_by_stem = {path.stem: path for path in saved_plots}

    lines = [
        "# RocketSim Telemetry Report",
        "",
        f"- Source CSV: `{csv_path}`",
        f"- Mode: `{mode}`",
        f"- X-axis: `{x_col}`",
        "",
        "## How to read the graphs",
        "",
    ]

    if mode == "episodes":
        lines.extend(
            [
                "Each episode point is computed across training areas. The blue line is the mean value for all areas "
                "at that episode number, and the shaded envelope is plus/minus one standard deviation across those "
                "areas. This makes the plot answer: did the whole training batch improve, and did the areas become "
                "more consistent?",
                "",
            ]
        )
    else:
        lines.extend(
            [
                "Step CSVs are noisy because they contain every physics tick. The main step plots now collapse those "
                "ticks into one readable value per episode, then draw a rolling trend line. This makes the plots answer: "
                "are travel, stabilization, and control getting better over training?",
                "",
                "The final step progress plot is still normalized from 0% to 100% of each episode. It shows what a "
                "typical episode looks like internally without using dense relationship maps.",
                "",
            ]
        )

    lines.extend(["## Graph descriptions", ""])

    ordered_stems = ["00_summary_dashboard", "00_performance_metrics"] + [filename for filename, _, _ in EPISODE_GROUPS]
    ordered_stems += [filename for filename, _, _ in STEP_EPISODE_GROUPS] + ["06_step_episode_progress_profiles"]

    for stem in ordered_stems:
        if stem not in saved_by_stem:
            continue
        title = title_for_stem(stem)
        lines.extend(
            [
                f"### {title}",
                "",
                GRAPH_DESCRIPTIONS.get(stem, "Generated telemetry plot."),
                "",
                f"![{title}]({saved_by_stem[stem].name})",
                "",
            ]
        )

    metric_lines = metric_description_lines(df, mode)
    if metric_lines:
        lines.extend(["## Metric meanings", ""])
        lines.extend(metric_lines)
        lines.append("")

    observations = build_observations(df, mode)
    if observations:
        lines.extend(["## Short graph analysis", ""])
        lines.extend(observations)
        lines.extend(
            [
                "",
                "Episode 0 can appear as zeros when logging starts before a meaningful completed policy episode has "
                "accumulated values, or when the first spawned agents reset very quickly. A sharp early jump is typical "
                "for RL because the policy first learns to avoid immediate terminal penalties. Later stagnation often "
                "means the easy reward terms are saturated, exploration has cooled, or the remaining improvement needs "
                "a more specific reward term/curriculum change.",
            ]
        )
        lines.append("")

    report_path.write_text("\n".join(lines), encoding="utf-8")
    return report_path


def title_for_stem(stem: str) -> str:
    if stem == "00_summary_dashboard":
        return "Summary Dashboard"
    if stem == "00_performance_metrics":
        return "Agent Performance Metrics"
    for filename, title, _ in EPISODE_GROUPS + STEP_EPISODE_GROUPS:
        if filename == stem:
            return title
    if stem == "06_step_episode_progress_profiles":
        return "Within-Episode Step Profiles"
    return stem.replace("_", " ").title()


def metric_description_lines(df: pd.DataFrame, mode: str) -> list[str]:
    lines = []
    seen = set()
    if mode == "steps":
        for _, _, metrics in STEP_EPISODE_GROUPS:
            for metric, label in metrics:
                if metric in seen:
                    continue
                seen.add(metric)
                explanation = STEP_DERIVED_EXPLANATIONS.get(metric)
                if explanation:
                    lines.append(f"- **{label}**: {explanation}")

    groups = EPISODE_GROUPS if mode == "episodes" else [("steps", "steps", STEP_TIMESERIES)]
    for _, _, metrics in groups:
        for metric, label in metrics:
            if metric in seen or metric_col(df, metric, mode) is None:
                continue
            seen.add(metric)
            explanation = METRIC_EXPLANATIONS.get(metric)
            if explanation:
                lines.append(f"- **{label}**: {explanation}")
    return lines


def build_observations(df: pd.DataFrame, mode: str) -> list[str]:
    observations = []
    working = df.copy()

    for metric, label, direction in ANALYSIS_METRICS:
        col = metric_col(working, metric, mode)
        if col is None:
            continue

        if mode == "episodes":
            stats = area_episode_stats(working, "Episode", col)
            values = stats["mean"].dropna() if not stats.empty else pd.Series(dtype=float)
        else:
            values = pd.to_numeric(working[col], errors="coerce").dropna()

        if len(values) < 6:
            continue

        if direction == "abs_lower":
            values = values.abs()

        window = max(3, min(20, len(values) // 10))
        early = values.head(window).mean()
        late = values.tail(window).mean()
        if pd.isna(early) or pd.isna(late):
            continue

        observations.append(describe_trend(label, early, late, direction))
    return observations


def describe_trend(label: str, early: float, late: float, direction: str) -> str:
    delta = late - early
    pct = "" if abs(early) < 1e-9 else f" ({delta / abs(early) * 100:+.1f}%)"
    early_text = format_number(early)
    late_text = format_number(late)

    if direction == "higher":
        outcome = "improved" if delta > 0 else "fell"
        explanation = "This is generally good for learning." if delta > 0 else "This may indicate instability, curriculum difficulty, or a reward conflict."
    elif direction in {"lower", "abs_lower"}:
        outcome = "improved" if delta < 0 else "rose"
        explanation = "This suggests the policy became more controlled." if delta < 0 else "This can happen during exploration, overshoot, or harder scenario settings."
    elif direction == "near_one":
        early_err = abs(early - 1.0)
        late_err = abs(late - 1.0)
        outcome = "moved closer to hover-like 1 g" if late_err < early_err else "moved farther from 1 g"
        explanation = "Near 1 g is expected during stable hover because thrust balances gravity."
    else:
        outcome = "changed"
        explanation = "Interpret this together with reward, episode length, and task difficulty."

    return f"- **{label}** {outcome}: early average `{early_text}` -> late average `{late_text}`{pct}. {explanation}"


def format_number(value: float) -> str:
    if abs(value) >= 1000:
        return f"{value:,.0f}"
    if abs(value) >= 100:
        return f"{value:.1f}"
    if abs(value) >= 10:
        return f"{value:.2f}"
    return f"{value:.3f}"


def base_metric_name(column: str) -> str:
    return column.removesuffix("_Mean")


def canonical_metric_name(metric: str) -> str:
    return ALIAS_TO_CANONICAL.get(metric, metric)


def x_axis_label(mode: str, x_col: str) -> str:
    if mode == "episodes":
        return EPISODE_X_LABEL
    if x_col == "GlobalStep":
        return "Global logged timestep index (sampled from steps CSV)"
    if x_col == "Step":
        return "Step index within episode"
    return x_col


def add_axis_caption(ax, text: str, width: int = 96) -> None:
    if not text:
        return
    wrapped = textwrap.fill(text, width=width)
    ax.text(
        0.0,
        -0.34,
        wrapped,
        transform=ax.transAxes,
        fontsize=8,
        color="#4a4f55",
        va="top",
        ha="left",
    )


def metric_caption(metric: str, label: str) -> str:
    metric = canonical_metric_name(metric)
    explanation = METRIC_EXPLANATIONS.get(metric)
    if explanation:
        return explanation
    return f"Shows {label} against {EPISODE_X_LABEL.lower()}."


def performance_caption(column: str, label: str) -> str:
    metric = base_metric_name(column)
    if metric == "Reward_Step":
        return "Shows the reward received per timestep, averaged across areas for each completed episode."
    if column == "StepCount":
        return "Shows how many physics steps each episode lasted; longer can mean better survival or slower completion depending on scenario."
    if column == "EpisodeDuration_s":
        return "Shows episode length converted to seconds using the Unity fixed timestep."
    return metric_caption(metric, label)


def step_episode_caption(metric: str, label: str) -> str:
    explanation = STEP_DERIVED_EXPLANATIONS.get(metric, f"Shows {label} derived from per-step telemetry.")
    return (
        f"{explanation} The gray line is each logged episode; the blue line is the rolling trend, "
        "so noisy step-level behavior becomes readable as training progress."
    )


def progress_caption(metric: str, label: str) -> str:
    metric = canonical_metric_name(metric)
    explanation = METRIC_EXPLANATIONS.get(metric, f"Shows {label} during a typical episode.")
    return (
        f"{explanation} X-axis is normalized episode progress, so 0% is episode start "
        "and 100% is episode end, not total training episodes."
    )


if __name__ == "__main__":
    main()

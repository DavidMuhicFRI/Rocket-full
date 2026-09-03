"""Generate the training and evaluation figures for the bachelor thesis.

This is a thesis-oriented companion to ``tools/plot_telemetry.py``.  It reuses
that tool's telemetry preparation, bootstrap filtering and schema aliases, but
uses chronological cumulative PPO decision steps on the x axis.  The original
tool's episode-number aggregation is useful for a single uninterrupted run;
chronological aggregation is required here because some thesis runs were
resumed and their per-area episode counters restarted.

Only rows with ``Completed == 1`` are used for learning curves. Unity writes a
final partial row for each active area when a training process closes;
including those rows would create artificial zero-valued spikes at revision
boundaries. Evaluation figures use only the frozen, named evaluation files.
"""

from __future__ import annotations

import argparse
import importlib.util
import math
from pathlib import Path
from types import ModuleType

import matplotlib as mpl
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd


ROCKET_NAVY = "#17324D"
ROCKET_BLUE = "#3E6F9E"
ROCKET_TEAL = "#25877A"
ROCKET_AMBER = "#D8952A"
ROCKET_RED = "#B84A4A"
ROCKET_GRID = "#D9E2E8"
ROCKET_PAPER = "#F5F8FA"


RUNS = {
    "hover": {
        "filename": "telemetry_HoverFixed_episodes.csv",
        "final_steps_m": 20.078171,
        "bin_width_m": 0.50,
        "start_utc": "2026-09-03T08:28:52.2479549Z",
    },
    "tracking": {
        "filename": "telemetry_HoverTrackSimple3_episodes.csv",
        "final_steps_m": 30.000039,
        "bin_width_m": 0.50,
    },
    "landing_l10": {
        "filename": "telemetry_L10_episodes.csv",
        "final_steps_m": 79.629752,
        "bin_width_m": 1.00,
        "start_utc": "2026-08-30T15:16:23.8145519Z",
    },
    "landing_l11": {
        "filename": "telemetry_L11_episodes.csv",
        "final_steps_m": 100.000086,
        "bin_width_m": 1.00,
        # The telemetry file also contains 106 rows from an accidental earlier
        # run that reused the L11 identifier.  The immutable run manifest marks
        # the start of the clean, evaluated session.
        "start_utc": "2026-08-31T20:07:34.5476198Z",
    },
}


EVALUATIONS = {
    "hover": "telemetry_HoverFixed_evaluation_20260903_134045",
    "tracking": "telemetry_HoverTrackSimple3_evaluation_20260824_192643",
    "landing_l10": "telemetry_L10_evaluation_20260831_191953",
    "landing_l11_90": "telemetry_L11_evaluation_20260901_210415",
    "landing_l11": "telemetry_L11_evaluation_20260901_235641",
}


def parse_args() -> argparse.Namespace:
    default_telemetry = (
        Path.home()
        / "AppData"
        / "LocalLow"
        / "DefaultCompany"
        / "Falcon9"
        / "Telemetry"
    )
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--telemetry-dir", type=Path, default=default_telemetry)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parent / "figures" / "training",
    )
    parser.add_argument(
        "--task",
        choices=("all", "hover", "tracking", "landing"),
        default="all",
        help="Generate all figures or only one task's figures.",
    )
    return parser.parse_args()


def load_plot_tool(repo_root: Path) -> ModuleType:
    tool_path = repo_root / "tools" / "plot_telemetry.py"
    spec = importlib.util.spec_from_file_location("rocket_plot_telemetry", tool_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not import telemetry tool: {tool_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def resolve_metric(tool: ModuleType, header: pd.DataFrame, metric: str) -> str:
    direct = metric if metric in header.columns else None
    resolved = direct or tool.metric_col(header, metric, "episodes")
    if resolved is None:
        raise KeyError(f"Telemetry metric is missing: {metric}")
    return resolved


def load_run(
    tool: ModuleType,
    csv_path: Path,
    final_steps_m: float,
    logical_metrics: list[str],
    start_utc: str | None = None,
) -> tuple[pd.DataFrame, dict[str, str], int]:
    header = pd.read_csv(csv_path, nrows=0)
    metric_columns = {
        metric: resolve_metric(tool, header, metric) for metric in logical_metrics
    }

    required = {
        "AreaIndex",
        "Episode",
        "WallTime",
        "StepCount",
        "DecisionPeriod",
        "Completed",
        "Success",
        *metric_columns.values(),
    }
    # Keep the support signals used by the original bootstrap-row detector.
    for metric in ("Reward_Step", "Load_GForce", "Fuel_Fraction", "Fuel_UsedKg"):
        support = tool.metric_col(header, metric, "episodes")
        if support is not None:
            required.add(support)

    frame = pd.read_csv(csv_path, usecols=lambda name: name in required)
    if start_utc is not None:
        wall_time = pd.to_datetime(frame["WallTime"], errors="coerce", utc=True)
        frame = frame[wall_time >= pd.Timestamp(start_utc)].copy()
    frame = tool.prepare_dataframe(frame, "episodes")
    frame = tool.filter_bootstrap_episodes(frame)

    incomplete_count = int((pd.to_numeric(frame["Completed"], errors="coerce") != 1).sum())
    frame = frame[pd.to_numeric(frame["Completed"], errors="coerce") == 1].copy()

    step_count = pd.to_numeric(frame["StepCount"], errors="coerce")
    decision_period = pd.to_numeric(frame["DecisionPeriod"], errors="coerce")
    decision_steps = step_count / decision_period.where(decision_period > 0)
    valid = decision_steps.replace([np.inf, -np.inf], np.nan).notna() & (decision_steps > 0)
    frame = frame[valid].copy()
    decision_steps = decision_steps[valid]

    # Completed episodes omit the unfinished episode present at a process stop.
    # Scale their cumulative position to the trainer's immutable final step count
    # so that revision boundaries align with the recorded 30M/50M checkpoints.
    cumulative = decision_steps.cumsum()
    frame["TrainingStepsM"] = cumulative / cumulative.iloc[-1] * final_steps_m
    return frame, metric_columns, incomplete_count


def binned_summary(
    frame: pd.DataFrame,
    column: str,
    final_steps_m: float,
    bin_width_m: float,
    *,
    absolute: bool = False,
    scale: float = 1.0,
    predicate: pd.Series | None = None,
    statistic: str = "median",
) -> pd.DataFrame:
    working = frame if predicate is None else frame[predicate]
    values = pd.to_numeric(working[column], errors="coerce")
    if absolute:
        values = values.abs()
    values = values * scale

    count = max(1, int(np.ceil(final_steps_m / bin_width_m)))
    edges = np.linspace(0.0, final_steps_m, count + 1)
    bin_id = np.clip(np.digitize(working["TrainingStepsM"], edges) - 1, 0, count - 1)
    temporary = pd.DataFrame({"bin": bin_id, "value": values}).dropna()

    grouped = temporary.groupby("bin")["value"]
    centre = grouped.mean() if statistic == "mean" else grouped.median()
    result = pd.DataFrame(
        {
            "x": (edges[:-1] + edges[1:]) / 2.0,
            "centre": centre.reindex(range(count)),
            "q25": grouped.quantile(0.25).reindex(range(count)),
            "q75": grouped.quantile(0.75).reindex(range(count)),
            "count": grouped.count().reindex(range(count), fill_value=0),
        }
    )
    # A centred three-bin rolling median suppresses asynchronous area noise
    # without hiding large-scale transitions between training revisions.
    for column_name in ("centre", "q25", "q75"):
        result[column_name] = result[column_name].rolling(3, center=True, min_periods=1).median()
    return result.dropna(subset=["centre"])


def style() -> None:
    mpl.rcParams.update(
        {
            "font.family": "DejaVu Sans",
            "font.size": 9.2,
            "axes.titlesize": 10.5,
            "axes.titleweight": "bold",
            "axes.labelsize": 9.2,
            "axes.edgecolor": ROCKET_NAVY,
            "axes.linewidth": 0.8,
            "xtick.color": ROCKET_NAVY,
            "ytick.color": ROCKET_NAVY,
            "text.color": ROCKET_NAVY,
            "pdf.fonttype": 42,
            "ps.fonttype": 42,
        }
    )


def prepare_axes(axes: np.ndarray) -> None:
    for index, ax in enumerate(axes.flat):
        ax.set_facecolor("white")
        ax.grid(axis="y", color=ROCKET_GRID, linewidth=0.7, alpha=0.95)
        ax.spines[["top", "right"]].set_visible(False)
        ax.text(
            0.015,
            0.96,
            chr(ord("A") + index),
            transform=ax.transAxes,
            va="top",
            ha="left",
            fontsize=9,
            fontweight="bold",
            color="white",
            bbox={"boxstyle": "round,pad=0.26", "facecolor": ROCKET_NAVY, "edgecolor": "none"},
            zorder=10,
        )


def plot_band(
    ax: plt.Axes,
    summary: pd.DataFrame,
    *,
    color: str,
    label: str | None = None,
    band: bool = True,
    linestyle: str = "-",
) -> None:
    if band:
        ax.fill_between(
            summary["x"],
            summary["q25"],
            summary["q75"],
            color=color,
            alpha=0.16,
            linewidth=0,
        )
    ax.plot(
        summary["x"],
        summary["centre"],
        color=color,
        linewidth=2.1,
        linestyle=linestyle,
        label=label,
    )


def finish_figure(fig: plt.Figure, output_base: Path) -> None:
    output_base.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output_base.with_suffix(".pdf"), bbox_inches="tight", facecolor="white")
    fig.savefig(output_base.with_suffix(".png"), dpi=220, bbox_inches="tight", facecolor="white")
    plt.close(fig)


def read_evaluation(telemetry_dir: Path, key: str) -> pd.DataFrame:
    path = telemetry_dir / f"{EVALUATIONS[key]}_episodes.csv"
    frame = pd.read_csv(path)
    duration = pd.to_numeric(frame.get("DurationSeconds"), errors="coerce")
    frame = frame[duration > 0].copy()
    if "TerminationReason" in frame.columns:
        frame = frame[frame["TerminationReason"].notna()].copy()
    return frame


def wilson_interval(successes: int, total: int) -> tuple[float, float, float]:
    """Return a percentage and its 95% Wilson interval."""
    if total <= 0:
        return math.nan, math.nan, math.nan
    z = 1.959963984540054
    p = successes / total
    denominator = 1.0 + z * z / total
    centre = (p + z * z / (2.0 * total)) / denominator
    half = z * math.sqrt(p * (1.0 - p) / total + z * z / (4.0 * total * total)) / denominator
    return 100.0 * p, 100.0 * (centre - half), 100.0 * (centre + half)


def set_band_axis(ax: plt.Axes, difficulties: np.ndarray) -> None:
    ax.set_xticks(np.arange(len(difficulties)))
    ax.set_xticklabels([f"{value:.2f}" for value in difficulties])
    ax.set_xlabel("curriculum difficulty")


def draw_boxplot(
    ax: plt.Axes,
    values: list[np.ndarray],
    *,
    scale: float = 1.0,
) -> None:
    scaled = [np.asarray(value, dtype=float) * scale for value in values]
    plot = ax.boxplot(
        scaled,
        positions=np.arange(len(scaled)),
        widths=0.58,
        patch_artist=True,
        showmeans=True,
        showfliers=False,
        medianprops={"color": ROCKET_NAVY, "linewidth": 1.5},
        meanprops={
            "marker": "D",
            "markerfacecolor": ROCKET_AMBER,
            "markeredgecolor": "white",
            "markersize": 4.5,
        },
        whiskerprops={"color": ROCKET_BLUE, "linewidth": 1.0},
        capprops={"color": ROCKET_BLUE, "linewidth": 1.0},
    )
    for box in plot["boxes"]:
        box.set_facecolor("#CFE5E1")
        box.set_edgecolor(ROCKET_TEAL)
        box.set_linewidth(1.0)


def plot_median_iqr(
    ax: plt.Axes,
    frame: pd.DataFrame,
    difficulties: np.ndarray,
    metric: str,
    *,
    color: str,
    label: str,
    offset: float,
    scale: float = 1.0,
) -> None:
    means: list[float] = []
    lower: list[float] = []
    upper: list[float] = []
    for difficulty in difficulties:
        values = pd.to_numeric(
            frame.loc[frame["CurriculumEpisodeDifficulty01"].eq(difficulty), metric],
            errors="coerce",
        ).dropna()
        values = values * scale
        median = float(values.median())
        means.append(median)
        lower.append(max(0.0, median - float(values.quantile(0.25))))
        upper.append(max(0.0, float(values.quantile(0.75)) - median))
    ax.errorbar(
        np.arange(len(difficulties)) + offset,
        means,
        yerr=np.vstack([lower, upper]),
        color=color,
        marker="o",
        markersize=4.5,
        linewidth=1.8,
        capsize=2.5,
        label=label,
    )


def make_hover(tool: ModuleType, telemetry_dir: Path, output_dir: Path) -> None:
    run = RUNS["hover"]
    metrics = [
        "DurationSeconds",
        "Goal_PlanarDistance_m",
        "Goal_AbsVerticalError_m",
        "Vel_Speed3D_mps",
        "Vel_VerticalSpeed_mps",
        "Att_TiltDeg",
        "FuelUsed_kg",
    ]
    frame, columns, incomplete = load_run(
        tool,
        telemetry_dir / run["filename"],
        run["final_steps_m"],
        metrics,
        run.get("start_utc"),
    )

    fig, axes = plt.subplots(3, 2, figsize=(7.15, 6.45), sharex=True, constrained_layout=True)
    prepare_axes(axes)
    panels = [
        ("DurationSeconds", "Survival time", "s", ROCKET_TEAL, False, 1.0),
        ("Goal_PlanarDistance_m", "Horizontal target error", "m", ROCKET_BLUE, False, 1.0),
        ("Goal_AbsVerticalError_m", "Vertical target error", "m", ROCKET_RED, False, 1.0),
        ("Vel_VerticalSpeed_mps", "Absolute vertical speed", "m/s", ROCKET_AMBER, True, 1.0),
        ("Att_TiltDeg", "Vehicle tilt", "deg", ROCKET_BLUE, False, 1.0),
        ("FuelUsed_kg", "Propellant per episode", "t", ROCKET_TEAL, False, 0.001),
    ]
    for ax, (metric, title, unit, color, absolute, scale) in zip(axes.flat, panels):
        summary = binned_summary(
            frame,
            columns[metric],
            run["final_steps_m"],
            run["bin_width_m"],
            absolute=absolute,
            scale=scale,
        )
        plot_band(ax, summary, color=color)
        ax.set_title(title, loc="left", pad=8)
        ax.set_ylabel(unit)
        ax.set_ylim(bottom=0)
    for ax in axes[2, :]:
        ax.set_xlabel("PPO decision steps (millions)")
    finish_figure(fig, output_dir / "training-hover")

    evaluation = read_evaluation(telemetry_dir, "hover")
    step_path = telemetry_dir / f"{EVALUATIONS['hover']}_steps.csv"
    step_metrics = [
        "AreaIndex",
        "Episode",
        "Step",
        "Goal_HorizontalDistanceToGoal_m",
        "Goal_AbsoluteVerticalDistanceToGoal_m",
        "Vel_Speed3D_mps",
        "Att_TiltDeg",
    ]
    steps = pd.read_csv(step_path, usecols=step_metrics)
    valid_episodes = set(pd.to_numeric(evaluation["Episode"], errors="coerce").astype(int))
    steps = steps[pd.to_numeric(steps["Episode"], errors="coerce").isin(valid_episodes)].copy()
    fixed_delta = float(pd.to_numeric(evaluation["FixedDeltaTimeSeconds"], errors="coerce").median())
    steps["TimeSeconds"] = pd.to_numeric(steps["Step"], errors="coerce") * fixed_delta
    # A 0.1-second grid keeps the vector figure compact while preserving the
    # response transient and the full 60-second settling profile.
    step_number = pd.to_numeric(steps["Step"], errors="coerce")
    steps = steps[(step_number.mod(10).eq(1)) | step_number.eq(step_number.max())].copy()

    fig, axes = plt.subplots(2, 2, figsize=(7.15, 4.65), sharex=True, constrained_layout=True)
    prepare_axes(axes)
    eval_panels = [
        ("Goal_HorizontalDistanceToGoal_m", "Horizontal target error", "m"),
        ("Goal_AbsoluteVerticalDistanceToGoal_m", "Vertical target error", "m"),
        ("Vel_Speed3D_mps", "Three-dimensional speed", "m/s"),
        ("Att_TiltDeg", "Vehicle tilt", "deg"),
    ]
    grouped = steps.groupby("Step", sort=True)
    for ax, (metric, title, unit) in zip(axes.flat, eval_panels):
        values = grouped[metric]
        x = grouped["TimeSeconds"].median()
        median = values.median()
        q25 = values.quantile(0.25)
        q75 = values.quantile(0.75)
        ax.fill_between(x, q25, q75, color=ROCKET_TEAL, alpha=0.18, linewidth=0)
        ax.plot(x, median, color=ROCKET_TEAL, linewidth=2.0)
        ax.scatter(x.iloc[-1], median.iloc[-1], s=20, color=ROCKET_AMBER, edgecolor="white", linewidth=0.6, zorder=5)
        ax.set_title(title, loc="left", pad=8)
        ax.set_ylabel(unit)
        ax.set_ylim(bottom=0)
    for ax in axes[1, :]:
        ax.set_xlabel("evaluation time (s)")
    finish_figure(fig, output_dir / "hover-evaluation")
    print(f"hover: {len(frame):,} completed episodes; excluded {incomplete} partial shutdown rows")


def make_tracking(tool: ModuleType, telemetry_dir: Path, output_dir: Path) -> None:
    run = RUNS["tracking"]
    metrics = [
        "CurriculumGlobalDifficulty01",
        "Success",
        "Track_DirectionEfficiency01",
        "Track_SettleQuality01",
        "DurationSeconds",
        "Goal_PlanarDistance_m",
        "Vel_PlanarSpeed_mps",
        "FuelUsed_kg",
    ]
    frame, columns, incomplete = load_run(
        tool,
        telemetry_dir / run["filename"],
        run["final_steps_m"],
        metrics,
        run.get("start_utc"),
    )

    fig, axes = plt.subplots(2, 2, figsize=(7.15, 4.55), sharex=True, constrained_layout=True)
    prepare_axes(axes)
    panels = [
        ("CurriculumGlobalDifficulty01", "Curriculum progress", ROCKET_TEAL, "median"),
        ("Success", "Training success rate", ROCKET_BLUE, "mean"),
        ("Track_DirectionEfficiency01", "Direction efficiency", ROCKET_AMBER, "median"),
        ("Track_SettleQuality01", "Settling quality", ROCKET_RED, "median"),
    ]
    for ax, (metric, title, color, statistic) in zip(axes.flat, panels):
        summary = binned_summary(
            frame,
            columns[metric],
            run["final_steps_m"],
            run["bin_width_m"],
            scale=100.0,
            statistic=statistic,
        )
        plot_band(ax, summary, color=color, band=metric != "Success")
        ax.set_title(title, loc="left", pad=8)
        ax.set_ylabel("%")
        ax.set_ylim(0, 105)
    for ax in axes[1, :]:
        ax.set_xlabel("PPO decision steps (millions)")
    finish_figure(fig, output_dir / "training-hover-track")

    fig, axes = plt.subplots(2, 2, figsize=(7.15, 4.55), sharex=True, constrained_layout=True)
    prepare_axes(axes)
    physical_panels = [
        ("DurationSeconds", "Episode duration", "s", ROCKET_TEAL, 1.0),
        ("Goal_PlanarDistance_m", "Mean horizontal target error", "m", ROCKET_BLUE, 1.0),
        ("Vel_PlanarSpeed_mps", "Mean horizontal speed", "m/s", ROCKET_AMBER, 1.0),
        ("FuelUsed_kg", "Propellant per episode", "t", ROCKET_RED, 0.001),
    ]
    for ax, (metric, title, unit, color, scale) in zip(axes.flat, physical_panels):
        summary = binned_summary(
            frame,
            columns[metric],
            run["final_steps_m"],
            run["bin_width_m"],
            scale=scale,
        )
        plot_band(ax, summary, color=color)
        ax.set_title(title, loc="left", pad=8)
        ax.set_ylabel(unit)
        ax.set_ylim(bottom=0)
    for ax in axes[1, :]:
        ax.set_xlabel("PPO decision steps (millions)")
    finish_figure(fig, output_dir / "tracking-training-physical")

    evaluation = read_evaluation(telemetry_dir, "tracking")
    difficulties = np.sort(evaluation["CurriculumEpisodeDifficulty01"].unique())
    positions = np.arange(len(difficulties))
    success = []
    success_low = []
    success_high = []
    captures = []
    duration_values = []
    fuel_values = []
    for difficulty in difficulties:
        band = evaluation[evaluation["CurriculumEpisodeDifficulty01"].eq(difficulty)]
        rate, low, high = wilson_interval(int(band["Success"].sum()), len(band))
        success.append(rate)
        success_low.append(max(0.0, rate - low))
        success_high.append(max(0.0, high - rate))
        captures.append(float(band["HoverTrackCaptures"].mean()))
        duration_values.append(pd.to_numeric(band["DurationSeconds"], errors="coerce").dropna().to_numpy())
        fuel_values.append(pd.to_numeric(band["FuelUsed_kg"], errors="coerce").dropna().to_numpy())

    fig, axes = plt.subplots(2, 2, figsize=(7.15, 4.75), constrained_layout=True)
    prepare_axes(axes)
    bar_colors = [ROCKET_TEAL, ROCKET_TEAL, ROCKET_TEAL, ROCKET_AMBER, ROCKET_RED]
    bars = axes[0, 0].bar(positions, success, width=0.62, color=bar_colors, alpha=0.88)
    axes[0, 0].errorbar(
        positions,
        success,
        yerr=np.vstack([success_low, success_high]),
        fmt="none",
        ecolor=ROCKET_NAVY,
        capsize=3,
        linewidth=1.1,
    )
    axes[0, 0].bar_label(bars, labels=[f"{value:.0f}%" for value in success], padding=3, fontsize=7.5)
    axes[0, 0].set_title("Successful three-capture episodes", loc="left", pad=8)
    axes[0, 0].set_ylabel("success rate (%)")
    axes[0, 0].set_ylim(0, 112)

    capture_bars = axes[0, 1].bar(positions, captures, width=0.62, color=bar_colors, alpha=0.88)
    axes[0, 1].bar_label(capture_bars, labels=[f"{value:.2f}" for value in captures], padding=3, fontsize=7.5)
    axes[0, 1].set_title("Targets captured per episode", loc="left", pad=8)
    axes[0, 1].set_ylabel("captures")
    axes[0, 1].set_ylim(0, 3.45)

    draw_boxplot(axes[1, 0], duration_values)
    axes[1, 0].set_title("Episode duration", loc="left", pad=8)
    axes[1, 0].set_ylabel("s")
    axes[1, 0].set_ylim(bottom=0)
    draw_boxplot(axes[1, 1], fuel_values, scale=0.001)
    axes[1, 1].set_title("Propellant used", loc="left", pad=8)
    axes[1, 1].set_ylabel("t")
    axes[1, 1].set_ylim(bottom=0)
    for ax in axes.flat:
        set_band_axis(ax, difficulties)
    finish_figure(fig, output_dir / "tracking-evaluation")
    print(f"tracking: {len(frame):,} completed episodes; excluded {incomplete} partial shutdown rows")


def make_landing(tool: ModuleType, telemetry_dir: Path, output_dir: Path) -> None:
    l10_run = RUNS["landing_l10"]
    l11_run = RUNS["landing_l11"]
    metrics = [
        "CurriculumGlobalDifficulty01",
        "Success",
        "LegTouchdownOccurred01",
        "LegFirstContactVerticalSpeed_mps",
        "EngineRestartCount",
    ]
    l10, l10_columns, l10_incomplete = load_run(
        tool,
        telemetry_dir / l10_run["filename"],
        l10_run["final_steps_m"],
        metrics,
        l10_run.get("start_utc"),
    )
    l11, l11_columns, l11_incomplete = load_run(
        tool,
        telemetry_dir / l11_run["filename"],
        l11_run["final_steps_m"],
        metrics,
        l11_run.get("start_utc"),
    )
    l10_touchdown = pd.to_numeric(l10[l10_columns["LegTouchdownOccurred01"]], errors="coerce").eq(1)
    l11_touchdown = pd.to_numeric(l11[l11_columns["LegTouchdownOccurred01"]], errors="coerce").eq(1)

    fig, axes = plt.subplots(2, 2, figsize=(7.15, 4.55), sharex=True, constrained_layout=True)
    prepare_axes(axes)
    run_specs = [
        ("L10", l10, l10_columns, l10_run, l10_touchdown, ROCKET_BLUE),
        ("L11", l11, l11_columns, l11_run, l11_touchdown, ROCKET_TEAL),
    ]
    comparison_panels = [
        (axes[0, 0], "CurriculumGlobalDifficulty01", "Effective curriculum", "%", 100.0, False, "median"),
        (axes[0, 1], "Success", "Training success", "%", 100.0, False, "mean"),
        (axes[1, 0], "LegFirstContactVerticalSpeed_mps", "Vertical speed at first contact", "m/s", 1.0, True, "median"),
        (axes[1, 1], "EngineRestartCount", "Engine restarts per episode", "restarts", 1.0, False, "median"),
    ]
    for run_name, frame, columns, run, touchdown, color in run_specs:
        for ax, metric, title, unit, scale, absolute, statistic in comparison_panels:
            summary = binned_summary(
                frame,
                columns[metric],
                run["final_steps_m"],
                run["bin_width_m"],
                scale=scale,
                absolute=absolute,
                predicate=touchdown if metric == "LegFirstContactVerticalSpeed_mps" else None,
                statistic=statistic,
            )
            plot_band(ax, summary, color=color, label=run_name if metric == "CurriculumGlobalDifficulty01" else None, band=metric != "Success")
    for ax, _, title, unit, _, _, _ in comparison_panels:
        ax.set_title(title, loc="left", pad=8)
        ax.set_ylabel(unit)
        ax.set_ylim(bottom=0)
    axes[0, 0].set_ylim(0, 105)
    axes[0, 1].set_ylim(0, 105)
    axes[0, 0].legend(frameon=False, loc="lower right", fontsize=8)
    axes[0, 0].annotate(
        "L10 ended",
        xy=(l10_run["final_steps_m"], 60),
        xytext=(69, 76),
        arrowprops={"arrowstyle": "-", "color": ROCKET_BLUE, "linewidth": 0.8},
        fontsize=7.2,
        color=ROCKET_BLUE,
    )
    for ax in axes.flat:
        ax.axvline(90.0, color=ROCKET_NAVY, linewidth=0.8, alpha=0.25, linestyle=":")
    axes[0, 1].text(90.0, 101.5, "L11 resume", ha="center", va="top", fontsize=7, color=ROCKET_NAVY)
    for ax in axes[1, :]:
        ax.set_xlabel("PPO decision steps (millions)")
    finish_figure(fig, output_dir / "training-leg-landing")

    l10_eval = read_evaluation(telemetry_dir, "landing_l10")
    l11_eval = read_evaluation(telemetry_dir, "landing_l11")
    difficulties = np.sort(l11_eval["CurriculumEpisodeDifficulty01"].unique())
    positions = np.arange(len(difficulties))
    fig, axes = plt.subplots(2, 2, figsize=(7.15, 4.75), constrained_layout=True)
    prepare_axes(axes)
    for label, frame, color, offset in [
        ("L10", l10_eval, ROCKET_BLUE, -0.10),
        ("L11", l11_eval, ROCKET_TEAL, 0.10),
    ]:
        rates = []
        low_errors = []
        high_errors = []
        for difficulty in difficulties:
            band = frame[frame["CurriculumEpisodeDifficulty01"].eq(difficulty)]
            rate, low, high = wilson_interval(int(band["Success"].sum()), len(band))
            rates.append(rate)
            low_errors.append(max(0.0, rate - low))
            high_errors.append(max(0.0, high - rate))
        axes[0, 0].errorbar(
            positions + offset,
            rates,
            yerr=np.vstack([low_errors, high_errors]),
            color=color,
            marker="o",
            markersize=4.8,
            linewidth=1.8,
            capsize=2.5,
            label=label,
        )
        plot_median_iqr(
            axes[0, 1], frame, difficulties, "EngineRestartCount", color=color, label=label, offset=offset
        )
        touchdown = frame[pd.to_numeric(frame["LegTouchdownTime_s"], errors="coerce") > 0].copy()
        plot_median_iqr(
            axes[1, 0], touchdown, difficulties, "LegFirstContactVerticalSpeed_mps", color=color, label=label, offset=offset
        )
        plot_median_iqr(
            axes[1, 1], touchdown, difficulties, "LegMaximumContactImpulse_Ns", color=color, label=label, offset=offset, scale=0.001
        )
    eval_panels = [
        (axes[0, 0], "Stable four-foot touchdown", "success rate (%)"),
        (axes[0, 1], "Engine switching", "median restarts"),
        (axes[1, 0], "First-contact vertical speed", "median m/s"),
        (axes[1, 1], "Maximum contact impulse", "median kN s"),
    ]
    for ax, title, unit in eval_panels:
        ax.set_title(title, loc="left", pad=8)
        ax.set_ylabel(unit)
        ax.set_ylim(bottom=0)
        set_band_axis(ax, difficulties)
    axes[0, 0].set_ylim(0, 108)
    axes[0, 0].legend(frameon=False, fontsize=8, loc="lower left")
    finish_figure(fig, output_dir / "landing-evaluation-comparison")

    l10_full = l10_eval[l10_eval["CurriculumEpisodeDifficulty01"].eq(1.0)].copy()
    l11_full = l11_eval[l11_eval["CurriculumEpisodeDifficulty01"].eq(1.0)].copy()
    l11_90 = read_evaluation(telemetry_dir, "landing_l11_90")
    l11_90_full = l11_90[l11_90["CurriculumEpisodeDifficulty01"].eq(1.0)].copy()
    fig, axes = plt.subplots(2, 2, figsize=(7.15, 4.85), constrained_layout=True)
    prepare_axes(axes)

    termination_order = [
        "LegLandingSuccessfulTouchdown",
        "LegLandingHardTouchdown",
        "LegLandingMissedPad",
        "LegLandingTooFarFromTarget",
    ]
    termination_labels = ["success", "hard contact", "missed pad", "too far"]
    termination_colors = [ROCKET_TEAL, ROCKET_RED, ROCKET_AMBER, ROCKET_BLUE]
    bottom = np.zeros(2)
    for reason, label, color in zip(termination_order, termination_labels, termination_colors):
        counts = np.array([
            int(l10_full["TerminationReason"].eq(reason).sum()),
            int(l11_full["TerminationReason"].eq(reason).sum()),
        ])
        bars = axes[0, 0].bar([0, 1], counts, bottom=bottom, color=color, width=0.62, label=label)
        for bar, count, base in zip(bars, counts, bottom):
            if count >= 3:
                axes[0, 0].text(
                    bar.get_x() + bar.get_width() / 2,
                    base + count / 2,
                    str(int(count)),
                    ha="center",
                    va="center",
                    fontsize=7.5,
                    color="white" if color in (ROCKET_TEAL, ROCKET_RED, ROCKET_BLUE) else ROCKET_NAVY,
                    fontweight="bold",
                )
        bottom += counts
    axes[0, 0].set_xticks([0, 1], ["L10", "L11"])
    axes[0, 0].set_ylim(0, 53)
    axes[0, 0].set_ylabel("episodes (out of 50)")
    axes[0, 0].set_title("Full-difficulty outcomes", loc="left", pad=8)
    axes[0, 0].legend(frameon=False, fontsize=6.8, ncol=2, loc="upper right")

    for label, frame, color in [("L10", l10_full, ROCKET_BLUE), ("L11", l11_full, ROCKET_TEAL)]:
        touchdown = frame[pd.to_numeric(frame["LegTouchdownTime_s"], errors="coerce") > 0]
        values = np.sort(pd.to_numeric(touchdown["LegFirstContactVerticalSpeed_mps"], errors="coerce").abs().dropna())
        percentage = 100.0 * np.arange(1, len(values) + 1) / len(values)
        axes[0, 1].step(values, percentage, where="post", color=color, linewidth=2.0, label=label)
    axes[0, 1].axvline(1.5, color=ROCKET_RED, linewidth=1.0, linestyle="--", label="vertical limit")
    axes[0, 1].set_title("First-contact vertical speed", loc="left", pad=8)
    axes[0, 1].set_xlabel("absolute vertical speed (m/s)")
    axes[0, 1].set_ylabel("touchdowns at or below (%)")
    axes[0, 1].set_xlim(0, 6)
    axes[0, 1].set_ylim(0, 103)
    axes[0, 1].text(
        5.82,
        55,
        "one contact from each run\nlies beyond this range",
        ha="right",
        va="bottom",
        fontsize=6.8,
        color=ROCKET_NAVY,
        bbox={"facecolor": "white", "edgecolor": "none", "alpha": 0.82, "pad": 1.5},
    )
    axes[0, 1].legend(frameon=False, fontsize=7.2, loc="lower right")

    l11_touchdown = l11_full[pd.to_numeric(l11_full["LegTouchdownTime_s"], errors="coerce") > 0]
    for succeeded, label, color, marker in [
        (0, "failed", ROCKET_RED, "x"),
        (1, "successful", ROCKET_TEAL, "o"),
    ]:
        subset = l11_touchdown[l11_touchdown["Success"].eq(succeeded)]
        axes[1, 0].scatter(
            pd.to_numeric(subset["LegFirstContactHorizontalSpeed_mps"], errors="coerce"),
            pd.to_numeric(subset["LegFirstContactVerticalSpeed_mps"], errors="coerce").abs(),
            s=24,
            color=color,
            marker=marker,
            alpha=0.85,
            label=label,
        )
    axes[1, 0].axhline(1.5, color=ROCKET_RED, linewidth=1.0, linestyle="--")
    axes[1, 0].axvline(0.75, color=ROCKET_AMBER, linewidth=1.0, linestyle=":")
    axes[1, 0].set_title("What separates L11 landings", loc="left", pad=8)
    axes[1, 0].set_xlabel("horizontal speed at contact (m/s)")
    axes[1, 0].set_ylabel("vertical speed at contact (m/s)")
    axes[1, 0].set_xlim(0, 1.0)
    axes[1, 0].set_ylim(0, 2.5)
    off_scale = int(
        (
            pd.to_numeric(l11_touchdown["LegFirstContactHorizontalSpeed_mps"], errors="coerce").abs().gt(1.0)
            | pd.to_numeric(l11_touchdown["LegFirstContactVerticalSpeed_mps"], errors="coerce").abs().gt(2.5)
        ).sum()
    )
    axes[1, 0].text(
        0.97,
        2.37,
        f"{off_scale} hard contacts off scale",
        ha="right",
        va="top",
        fontsize=6.8,
        color=ROCKET_RED,
        bbox={"facecolor": "white", "edgecolor": "none", "alpha": 0.82, "pad": 1.5},
    )
    axes[1, 0].legend(frameon=False, fontsize=7.2, loc="upper left")

    paired = l11_90_full[["EpisodeSeed", "Success"]].merge(
        l11_full[["EpisodeSeed", "Success"]],
        on="EpisodeSeed",
        suffixes=("_90", "_100"),
        validate="one_to_one",
    )
    transition_masks = [
        paired["Success_90"].eq(1) & paired["Success_100"].eq(1),
        paired["Success_90"].eq(0) & paired["Success_100"].eq(1),
        paired["Success_90"].eq(1) & paired["Success_100"].eq(0),
        paired["Success_90"].eq(0) & paired["Success_100"].eq(0),
    ]
    transition_counts = [int(mask.sum()) for mask in transition_masks]
    transition_labels = ["stayed\nsuccessful", "improved", "regressed", "stayed\nfailed"]
    transition_colors = [ROCKET_TEAL, "#64A99D", ROCKET_AMBER, ROCKET_RED]
    bars = axes[1, 1].bar(np.arange(4), transition_counts, color=transition_colors, width=0.66)
    axes[1, 1].bar_label(bars, padding=3, fontsize=8, fontweight="bold")
    axes[1, 1].set_xticks(np.arange(4), transition_labels)
    axes[1, 1].set_title("Same seeds: 90M to 100M", loc="left", pad=8)
    axes[1, 1].set_ylabel("episodes (out of 50)")
    axes[1, 1].set_ylim(0, max(transition_counts) * 1.22)
    finish_figure(fig, output_dir / "landing-full-difficulty")

    print(
        "landing: "
        f"L10 {len(l10):,} completed episodes ({l10_incomplete} partial rows excluded); "
        f"L11 {len(l11):,} completed episodes ({l11_incomplete} partial rows excluded)"
    )


def main() -> None:
    args = parse_args()
    repo_root = Path(__file__).resolve().parents[1]
    tool = load_plot_tool(repo_root)
    style()
    if args.task in ("all", "hover"):
        make_hover(tool, args.telemetry_dir, args.output_dir)
    if args.task in ("all", "tracking"):
        make_tracking(tool, args.telemetry_dir, args.output_dir)
    if args.task in ("all", "landing"):
        make_landing(tool, args.telemetry_dir, args.output_dir)
    print(f"Saved thesis figures to {args.output_dir}")


if __name__ == "__main__":
    main()

# Bachelor thesis working draft

The editable entry point is `thesis.tex`. The source follows the official University of
Ljubljana FRI bachelor-thesis book template, while the body is temporarily written in English.

Build with pdfLaTeX and Biber:

1. `pdflatex thesis.tex`
2. `biber thesis`
3. `pdflatex thesis.tex`
4. `pdflatex thesis.tex`

## Open in Overleaf

The easiest route is to upload the packaged source ZIP as a project:

1. In Overleaf, choose **New Project > Upload Project** and select the LaTeX source ZIP.
2. Open **Menu**, set **Main document** to `thesis.tex` if it was not detected automatically,
   and select **pdfLaTeX** as the compiler.
3. Click **Recompile**. Overleaf detects `biblatex` and runs Biber for the references.

If files are uploaded individually instead, preserve the directory structure. The required
source is `thesis.tex`, `references.bib`, every `.tex` file in `chapters/`, and the complete
`podporno/` and `figures/training/` directories. Generated files such as `.aux`, `.bcf`, `.bbl`,
`.log`, `.out`, `.toc` and the locally compiled thesis PDF are not required. The eight generated
figure PDFs are already included, so Overleaf does not need Python or the telemetry CSV files.

## Regenerate the training figures locally

`generate_training_figures.py` is a thesis-oriented companion to `tools/plot_telemetry.py`. It
reuses the plotting tool's telemetry schema and bootstrap filtering, but orders completed
episodes chronologically so trainer resumes cannot mix repeated episode indices. From the
repository root, run:

`python thesis/generate_training_figures.py`

Use `--telemetry-dir` if the Unity telemetry directory is not in its Windows default location.
Use `--task hover`, `--task tracking` or `--task landing` to regenerate only one task's figures.
The script reads the completed `HoverFixed`, `HoverTrackSimple3`, `L10` and `L11` training
episode CSVs together with the named fixed evaluation files. It writes vector PDF and PNG
figures under `thesis/figures/training/`. Only the small hover evaluation requires a step-level
CSV, because its 250 time-aligned trajectories are summarized as median and interquartile
profiles; the tracking and landing evaluation plots use episode-level records. L10 and L11 are
filtered by immutable run-manifest times so an accidental run-ID reuse cannot enter the plots.

Before submission, search the source tree for `TODO before submission` and `TODO:`. The completed
L10 baseline and final L11 leg-landing evaluations, together with the L1--L11 development
narrative, are included. Generated plots are based on the named run artifacts. The repository
revision and public URL must be frozen and inserted after final source changes are complete.

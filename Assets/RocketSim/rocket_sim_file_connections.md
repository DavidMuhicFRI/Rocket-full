# RocketSim documentation index

The maintained project documentation lives under the repository-level `docs/` directory:

- `../../docs/simulator_user_workflow.md` - vehicle presets, tasks, training, evaluation, and example workflows
- `../../docs/evaluation_protocol.md` - deterministic standard-evaluation contract and output artifacts
- `../../TrainingConfig.yaml` - synchronized manual-CLI PPO reference; UI launches use the generated run-local copy
- `../../docs/landing_curriculum.md` - independent continuous curricula for chopstick and physical leg landing
- `../../docs/reward_functions.md` - index of reward implementations and the detailed Unity-visible reward guide
- `../../docs/rocket_sim_architecture_overview.puml` - compact ownership/dependency diagram source
- `../../docs/rocket_sim_architecture_visual.puml` - detailed file-level architecture diagram source

The executable reward narrative is `Scripts/Core/Rewards/reward_functions.md`.
Each training result directory stores the latest complete objective in
`RewardConfig.json`, an append-only launch record in
`RewardConfigHistory.jsonl`, and its current hash/revision in `RunManifest.json`.
Generated PNG/SVG architecture diagrams sit beside their PlantUML sources. Update
both diagram sources and renderings whenever runtime ownership or major data flow
changes.

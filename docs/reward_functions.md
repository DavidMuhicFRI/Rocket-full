# Reward functions

The detailed Unity-visible reward narrative is currently stored at:

`../Assets/RocketSim/Scripts/Core/Rewards/reward_functions.md`

The executable source of truth is:

- `../Assets/RocketSim/Scripts/Core/Rewards/RocketRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/LandingRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/HoverRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/TakeoffRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/BellyFlopRewardModel.cs`

Known documentation drift is tracked in [`codebase-audit.md`](codebase-audit.md). Before using formulas in the thesis, generate the narrative/table from named reward components or cover every documented formula and terminal threshold with tests.

The landing curriculum design and comparison modes are documented in
[`landing_curriculum.md`](landing_curriculum.md).

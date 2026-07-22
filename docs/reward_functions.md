# Reward functions

The detailed Unity-visible reward narrative is currently stored at:

`../Assets/RocketSim/Scripts/Core/Rewards/reward_functions.md`

The executable source of truth is:

- `../Assets/RocketSim/Scripts/Core/Rewards/RocketRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/ChopstickLandingRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/LegLandingRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/HoverRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/TakeoffRewardModel.cs`
- `../Assets/RocketSim/Scripts/Core/Rewards/BellyFlopRewardModel.cs`

The guide distinguishes the logical chopstick catch from physical leg landing and
documents the current baseline coefficients. Treat executable code and regression
tests as authoritative if a reward is changed during experiment development.

The landing curriculum design and comparison modes are documented in
[`landing_curriculum.md`](landing_curriculum.md).

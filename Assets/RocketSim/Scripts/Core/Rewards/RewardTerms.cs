// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Rewards/RewardTerms.cs
// Purpose: Carries normalized, scenario-independent motion and control measures
// from FalconAgent to the selected reward model.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

namespace RocketSim
{
    /// <summary>
    /// One-step measurements shared by all reward models. Keeping them as plain
    /// data makes reward equations easy to inspect and test outside the agent.
    /// </summary>
    public struct RewardTerms
    {
        public float distance3D;
        public float planarDistance;
        public float verticalError;
        public float speed;
        public float planarSpeed;
        public float verticalSpeed;
        public float goalClosureRate;
        public float horizontalClosureRate;
        public float upDot;
        public float upright01;
        public float angularRateDegS;
        public float yawRateDegS;
        public float yawErrorDeg;
        public float controlEffort;
    }}

// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.LegLanding.cs
// Purpose: Bridges FalconAgent motion and Unity callbacks to LegLandingRuntime.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        /// <summary>Configures physical landing hardware for the selected task.</summary>
        void ConfigureLegLandingHardware()
        {
            bool active = envConfig != null && envConfig.scenario == ScenarioType.LegLanding;
            _legLanding.ConfigureHardware(
                active,
                assembly,
                targetPad,
                rb,
                cfg.radius,
                cfg.length,
                _defaultSolverIterations,
                _defaultSolverVelocityIterations,
                _defaultCollisionDetectionMode);
        }

        /// <summary>Places the root so the foot plane reaches a requested coordinate.</summary>
        void PlaceLegFeetFrameAtLocalPosition(Vector3 desiredFeetPosition)
        {
            ConfigureLegLandingHardware();
            Transform feetFrame = _legLanding.Legs ? _legLanding.Legs.FeetFrame : null;
            if (!feetFrame)
            {
                transform.localPosition = desiredFeetPosition;
                return;
            }

            Vector3 worldOffset = feetFrame.position - transform.position;
            Vector3 localOffset = transform.parent
                ? transform.parent.InverseTransformVector(worldOffset)
                : worldOffset;
            transform.localPosition = desiredFeetPosition - localOffset;
        }

        void CommitLegLandingContactFrame() => _legLanding.CommitContactFrame();

        void ResetLegLandingState() => _legLanding.Reset();

        /// <summary>Supplies current motion to the task-owned touchdown evaluator.</summary>
        void UpdateLegLandingState(float dt)
        {
            bool active = envConfig != null && envConfig.scenario == ScenarioType.LegLanding;
            if (!active)
            {
                _legLanding.Step(false, default, 0f, default, null, 0f, 0f, dt);
                return;
            }

            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            TerminationParameters termination =
                envConfig.GetTrainingObjective(ScenarioType.LegLanding).terminations;
            _legLanding.Step(
                true,
                terms,
                Vector3.Angle(transform.up, Vector3.up),
                ActiveLandingProfile,
                termination,
                _objectiveDifficulty01,
                LegFeetHeightAbovePad(),
                dt);
        }

        float LegFeetHeightAbovePad() =>
            ScenarioReferenceLocalPosition().y -
            ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig).y;

        bool IsLandingFootOnPad(int index) => _legLanding.IsFootOnPad(index);

        void OnCollisionEnter(Collision collision) => RecordLegLandingCollision(collision, null);
        void OnCollisionStay(Collision collision) => RecordLegLandingCollision(collision, null);

        /// <summary>Receives a pad contact directly from one landing-gear collider.</summary>
        internal void RecordLandingGearCollision(
            Collision collision,
            LandingGearCollider sourceMarker) =>
            RecordLegLandingCollision(collision, sourceMarker);

        void RecordLegLandingCollision(Collision collision, LandingGearCollider sourceMarker)
        {
            if (envConfig == null || envConfig.scenario != ScenarioType.LegLanding)
                return;

            _legLanding.RecordCollision(
                collision,
                sourceMarker,
                rb,
                transform,
                _episodeElapsedSeconds,
                LegFeetHeightAbovePad());
        }
    }
}

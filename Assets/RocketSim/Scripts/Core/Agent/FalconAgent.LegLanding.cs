// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.LegLanding.cs
// Purpose: Configures physical landing gear, aggregates compound-collider pad
// contacts, and evaluates the stable multi-foot touchdown state.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        const int LegLandingSolverIterations = 12;
        const int LegLandingSolverVelocityIterations = 4;

        /// <summary>
        /// Ensures physical gear/pad markers exist, toggles the gear by scenario,
        /// and applies stronger per-body contact settings only when needed.
        /// </summary>
        void ConfigureLegLandingHardware()
        {
            _landingLegs ??= assembly ? assembly.LandingLegs : null;
            if (!_landingLegs)
                Debug.LogError("[FalconAgent] LandingLegComponent is missing from RocketAssembly.", this);
            _landingPadSurface = LandingPadSurface.Ensure(targetPad);

            bool active = envConfig != null && envConfig.scenario == ScenarioType.LegLanding;
            _landingLegs?.Configure(active, cfg.radius, cfg.length);
            if (!rb) return;

            rb.solverIterations = active ? LegLandingSolverIterations : _defaultSolverIterations;
            rb.solverVelocityIterations = active
                ? LegLandingSolverVelocityIterations
                : _defaultSolverVelocityIterations;
            rb.collisionDetectionMode = active
                ? CollisionDetectionMode.ContinuousDynamic
                : _defaultCollisionDetectionMode;
        }

        /// <summary>Places the root so the landing-foot plane reaches a requested local coordinate.</summary>
        void PlaceLegFeetFrameAtLocalPosition(Vector3 desiredFeetPosition)
        {
            ConfigureLegLandingHardware();
            if (!_landingLegs || !_landingLegs.FeetFrame)
            {
                transform.localPosition = desiredFeetPosition;
                return;
            }

            Vector3 worldOffset = _landingLegs.FeetFrame.position - transform.position;
            Vector3 localOffset = transform.parent
                ? transform.parent.InverseTransformVector(worldOffset)
                : worldOffset;
            transform.localPosition = desiredFeetPosition - localOffset;
        }

        /// <summary>
        /// Promotes contacts collected by the previous Unity physics solve into
        /// the immutable state consumed during this FixedUpdate.
        /// </summary>
        void CommitLegLandingContactFrame()
        {
            _legFootMask = _legPendingFootMask;
            _legFirstContactThisStep = _legPendingFirstContact;
            _legFootOutsidePad |= _legPendingFootOutsidePad;
            _legStructuralStrike |= _legPendingStructuralStrike;

            _legPendingFootMask = 0;
            _legPendingFirstContact = false;
            _legPendingFootOutsidePad = false;
            _legPendingStructuralStrike = false;
        }

        /// <summary>Clears all episode-scoped touchdown/contact measurements.</summary>
        void ResetLegLandingState()
        {
            _legPendingFootMask = 0;
            _legFootMask = 0;
            _legPendingFootOutsidePad = false;
            _legFootOutsidePad = false;
            _legPendingStructuralStrike = false;
            _legStructuralStrike = false;
            _legPendingFirstContact = false;
            _legFirstContactThisStep = false;
            _legTouchdownStarted = false;
            _legStable = false;
            _legBecameStable = false;
            _legStableTime = 0f;
            _legTouchdownTime = 0f;
            _legFirstContactSpeed = 0f;
            _legFirstContactVerticalSpeed = 0f;
            _legFirstContactHorizontalSpeed = 0f;
            _legFirstContactTiltDeg = 0f;
            _legFirstContactAngularRateDegS = 0f;
            _legFirstContactHeightAbovePad = 0f;
            _legAllFeetContactLossTime = 0f;
            _legMaximumContactImpulseNs = 0f;
            _legMaximumReboundHeightM = 0f;
            _legExcessiveRebound = false;
        }

        /// <summary>Updates the continuous stable-hold timer from current contact and motion.</summary>
        void UpdateLegLandingState(float dt)
        {
            _legBecameStable = false;
            if (envConfig == null || envConfig.scenario != ScenarioType.LegLanding)
            {
                _legStable = false;
                _legStableTime = 0f;
                return;
            }

            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            float tiltDeg = Vector3.Angle(transform.up, Vector3.up);
            TerminationParameters termination =
                envConfig.GetTrainingObjective(ScenarioType.LegLanding).terminations;
            bool ready = LegLandingContactEvaluator.IsStableCandidate(
                _legFootMask,
                _legFootOutsidePad,
                _legStructuralStrike,
                terms,
                tiltDeg,
                ActiveLandingProfile,
                termination.legMinimumStableFeet);

            bool wasStable = _legStable;
            _legStableTime = ready ? _legStableTime + Mathf.Max(0f, dt) : 0f;
            float requiredHold = termination.landingStableHoldSeconds.At(_objectiveDifficulty01);
            _legStable = ready && _legStableTime >= Mathf.Max(0f, requiredHold);
            _legBecameStable = !wasStable && _legStable;

            if (_legTouchdownStarted)
            {
                float reboundRise = Mathf.Max(
                    0f,
                    LegFeetHeightAbovePad() - _legFirstContactHeightAbovePad);
                _legMaximumReboundHeightM = Mathf.Max(_legMaximumReboundHeightM, reboundRise);
                _legAllFeetContactLossTime = _legFootMask != 0
                    ? 0f
                    : _legAllFeetContactLossTime + Mathf.Max(0f, dt);
                _legExcessiveRebound |= LegLandingContactEvaluator.IsExcessiveRebound(
                    _legMaximumReboundHeightM,
                    _legAllFeetContactLossTime,
                    termination.legMaximumReboundRiseM,
                    termination.legMaximumAllFeetContactLossSeconds);
            }
        }

        float LegFeetHeightAbovePad() =>
            ScenarioReferenceLocalPosition().y -
            ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig).y;

        bool IsLandingFootOnPad(int index) =>
            index >= 0 && index < LandingLegComponent.LegCount &&
            (_legFootMask & (1 << index)) != 0;

        void OnCollisionEnter(Collision collision) => RecordLegLandingCollision(collision, null);
        void OnCollisionStay(Collision collision) => RecordLegLandingCollision(collision, null);

        /// <summary>Receives a pad contact directly from one generated child collider.</summary>
        internal void RecordLandingGearCollision(
            Collision collision,
            LandingGearCollider sourceMarker) =>
            RecordLegLandingCollision(collision, sourceMarker);

        /// <summary>
        /// Records only collisions against this training area's landing pad.
        /// Contact callbacks fill a pending frame so reward evaluation never
        /// depends on Unity's callback ordering inside the current physics step.
        /// </summary>
        void RecordLegLandingCollision(Collision collision, LandingGearCollider sourceMarker)
        {
            if (envConfig == null || envConfig.scenario != ScenarioType.LegLanding || collision == null)
                return;
            if (!_landingPadSurface ||
                collision.collider.GetComponentInParent<LandingPadSurface>() != _landingPadSurface)
                return;

            _legMaximumContactImpulseNs = Mathf.Max(
                _legMaximumContactImpulseNs,
                collision.impulse.magnitude);

            int contactCount = collision.contactCount;
            for (int i = 0; i < contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                LandingGearCollider marker = contact.thisCollider
                    ? contact.thisCollider.GetComponent<LandingGearCollider>()
                    : null;

                // A child callback can contain the compound rigidbody's full
                // contact set. Process only points owned by that child; the root
                // callback separately classifies body and other structural hits.
                if (sourceMarker && marker != sourceMarker)
                    continue;

                if (!marker || marker.kind != LandingGearColliderKind.Foot)
                {
                    _legPendingStructuralStrike = true;
                    continue;
                }

                int bit = 1 << marker.footIndex;
                _legPendingFootMask |= bit;
                Transform foot = _landingLegs ? _landingLegs.FootTransform(marker.footIndex) : null;
                if (!foot || !_landingPadSurface.ContainsFootCenter(
                        foot.position,
                        _landingLegs.FootEdgeMarginM))
                    _legPendingFootOutsidePad = true;

                if (_legTouchdownStarted || _legPendingFirstContact)
                    continue;

                _legTouchdownStarted = true;
                _legPendingFirstContact = true;
                _legTouchdownTime = _episodeElapsedSeconds;
                Vector3 contactVelocity = rb.GetPointVelocity(contact.point);
                _legFirstContactSpeed = contactVelocity.magnitude;
                _legFirstContactVerticalSpeed = contactVelocity.y;
                _legFirstContactHorizontalSpeed = new Vector2(contactVelocity.x, contactVelocity.z).magnitude;
                _legFirstContactTiltDeg = Vector3.Angle(transform.up, Vector3.up);
                _legFirstContactAngularRateDegS = rb.angularVelocity.magnitude * Mathf.Rad2Deg;
                _legFirstContactHeightAbovePad = LegFeetHeightAbovePad();
            }
        }
    }
}

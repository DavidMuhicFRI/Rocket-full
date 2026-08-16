// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Tasks/Landing/LegLandingRuntime.cs
// Purpose: Owns physical leg-touchdown contacts and episode measurements.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    /// <summary>
    /// Converts Unity collision callbacks into a stable, frame-based landing
    /// state. FalconAgent only forwards collisions and supplies current motion.
    /// </summary>
    internal sealed class LegLandingRuntime
    {
        int _pendingFootMask;
        bool _pendingFootOutsidePad;
        bool _pendingStructuralStrike;
        bool _pendingFirstContact;
        float _firstContactHeightAbovePad;
        float _allFeetContactLossTime;

        public LandingLegComponent Legs { get; private set; }
        public LandingPadSurface PadSurface { get; private set; }
        public int FootMask { get; private set; }
        public bool FootOutsidePad { get; private set; }
        public bool StructuralStrike { get; private set; }
        public bool FirstContactThisStep { get; private set; }
        public bool TouchdownStarted { get; private set; }
        public bool Stable { get; private set; }
        public bool BecameStable { get; private set; }
        public float StableTime { get; private set; }
        public float TouchdownTime { get; private set; }
        public float FirstContactSpeed { get; private set; }
        public float FirstContactVerticalSpeed { get; private set; }
        public float FirstContactHorizontalSpeed { get; private set; }
        public float FirstContactTiltDeg { get; private set; }
        public float FirstContactAngularRateDegS { get; private set; }
        public float MaximumContactImpulseNs { get; private set; }
        public float MaximumReboundHeightM { get; private set; }
        public bool ExcessiveRebound { get; private set; }

        /// <summary>Finds scene components and enables gear only for leg landing.</summary>
        public void ConfigureHardware(
            bool enabled,
            RocketAssembly assembly,
            Transform targetPad,
            Rigidbody body,
            float bodyRadius,
            float bodyLength,
            int defaultSolverIterations,
            int defaultSolverVelocityIterations,
            CollisionDetectionMode defaultCollisionMode)
        {
            Legs ??= assembly ? assembly.LandingLegs : null;
            if (!Legs)
                Debug.LogError("[LegLandingRuntime] RocketAssembly needs a LandingLegComponent.", assembly);

            PadSurface = LandingPadSurface.Ensure(targetPad);
            Legs?.Configure(enabled, bodyRadius, bodyLength);
            if (!body) return;

            // Landing contacts need a few more solver passes than free flight.
            body.solverIterations = enabled ? 12 : defaultSolverIterations;
            body.solverVelocityIterations = enabled ? 4 : defaultSolverVelocityIterations;
            body.collisionDetectionMode = enabled
                ? CollisionDetectionMode.ContinuousDynamic
                : defaultCollisionMode;
        }

        /// <summary>Publishes contacts from the preceding physics solve.</summary>
        public void CommitContactFrame()
        {
            FootMask = _pendingFootMask;
            FirstContactThisStep = _pendingFirstContact;
            FootOutsidePad |= _pendingFootOutsidePad;
            StructuralStrike |= _pendingStructuralStrike;

            _pendingFootMask = 0;
            _pendingFirstContact = false;
            _pendingFootOutsidePad = false;
            _pendingStructuralStrike = false;
        }

        /// <summary>Clears every measurement that belongs to one episode.</summary>
        public void Reset()
        {
            _pendingFootMask = 0;
            FootMask = 0;
            _pendingFootOutsidePad = false;
            FootOutsidePad = false;
            _pendingStructuralStrike = false;
            StructuralStrike = false;
            _pendingFirstContact = false;
            FirstContactThisStep = false;
            TouchdownStarted = false;
            Stable = false;
            BecameStable = false;
            StableTime = 0f;
            TouchdownTime = 0f;
            FirstContactSpeed = 0f;
            FirstContactVerticalSpeed = 0f;
            FirstContactHorizontalSpeed = 0f;
            FirstContactTiltDeg = 0f;
            FirstContactAngularRateDegS = 0f;
            _firstContactHeightAbovePad = 0f;
            _allFeetContactLossTime = 0f;
            MaximumContactImpulseNs = 0f;
            MaximumReboundHeightM = 0f;
            ExcessiveRebound = false;
        }

        /// <summary>Updates touchdown stability and rebound measurements.</summary>
        public void Step(
            bool enabled,
            RewardTerms terms,
            float tiltDeg,
            LandingCurriculumProfile profile,
            TerminationParameters termination,
            float difficulty01,
            float feetHeightAbovePad,
            float deltaTime)
        {
            BecameStable = false;
            if (!enabled)
            {
                Stable = false;
                StableTime = 0f;
                return;
            }

            bool ready = LegLandingContactEvaluator.IsStableCandidate(
                FootMask,
                FootOutsidePad,
                StructuralStrike,
                terms,
                tiltDeg,
                profile,
                termination.legMinimumStableFeet);

            bool wasStable = Stable;
            float dt = Mathf.Max(0f, deltaTime);
            StableTime = ready ? StableTime + dt : 0f;
            float requiredHold = termination.landingStableHoldSeconds.At(difficulty01);
            Stable = ready && StableTime >= Mathf.Max(0f, requiredHold);
            BecameStable = !wasStable && Stable;

            if (!TouchdownStarted) return;

            float reboundRise = Mathf.Max(0f, feetHeightAbovePad - _firstContactHeightAbovePad);
            MaximumReboundHeightM = Mathf.Max(MaximumReboundHeightM, reboundRise);
            _allFeetContactLossTime = FootMask != 0 ? 0f : _allFeetContactLossTime + dt;
            ExcessiveRebound |= LegLandingContactEvaluator.IsExcessiveRebound(
                MaximumReboundHeightM,
                _allFeetContactLossTime,
                termination.legMaximumReboundRiseM,
                termination.legMaximumAllFeetContactLossSeconds);
        }

        public bool IsFootOnPad(int index) =>
            index >= 0 && index < LandingLegComponent.LegCount &&
            (FootMask & (1 << index)) != 0;

        /// <summary>Buffers contacts against this area's landing pad.</summary>
        public void RecordCollision(
            Collision collision,
            LandingGearCollider sourceMarker,
            Rigidbody body,
            Transform rocketTransform,
            float episodeElapsedSeconds,
            float feetHeightAbovePad)
        {
            if (collision == null || !PadSurface || !body || !rocketTransform)
                return;
            if (collision.collider.GetComponentInParent<LandingPadSurface>() != PadSurface)
                return;

            MaximumContactImpulseNs = Mathf.Max(MaximumContactImpulseNs, collision.impulse.magnitude);

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                LandingGearCollider marker = contact.thisCollider
                    ? contact.thisCollider.GetComponent<LandingGearCollider>()
                    : null;

                // Child callbacks can contain the compound body's complete
                // contact set. Each child handles only its own points.
                if (sourceMarker && marker != sourceMarker)
                    continue;

                if (!marker || marker.kind != LandingGearColliderKind.Foot)
                {
                    _pendingStructuralStrike = true;
                    continue;
                }

                _pendingFootMask |= 1 << marker.footIndex;
                Transform foot = Legs ? Legs.FootTransform(marker.footIndex) : null;
                if (!foot || !PadSurface.ContainsFootCenter(foot.position, Legs.FootEdgeMarginM))
                    _pendingFootOutsidePad = true;

                if (TouchdownStarted || _pendingFirstContact)
                    continue;

                TouchdownStarted = true;
                _pendingFirstContact = true;
                TouchdownTime = episodeElapsedSeconds;
                Vector3 contactVelocity = body.GetPointVelocity(contact.point);
                FirstContactSpeed = contactVelocity.magnitude;
                FirstContactVerticalSpeed = contactVelocity.y;
                FirstContactHorizontalSpeed = new Vector2(contactVelocity.x, contactVelocity.z).magnitude;
                FirstContactTiltDeg = Vector3.Angle(rocketTransform.up, Vector3.up);
                FirstContactAngularRateDegS = body.angularVelocity.magnitude * Mathf.Rad2Deg;
                _firstContactHeightAbovePad = feetHeightAbovePad;
            }
        }
    }
}

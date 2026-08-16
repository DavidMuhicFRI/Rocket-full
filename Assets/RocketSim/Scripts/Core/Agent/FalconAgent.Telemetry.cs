// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Agent/FalconAgent.Telemetry.cs
// Purpose: Measures reward terms and flight values and sends telemetry rows to the logger.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

namespace RocketSim
{
    public partial class FalconAgent : Agent
    {
        /// <summary>
        /// Measures the shared state values used by rewards and telemetry.
        /// </summary>
        RewardTerms MeasureRewardTerms(Vector3 goal)
        {
            Vector3 guidancePosition = ScenarioReferenceLocalPosition();
            Vector3 guidanceVelocity = ScenarioReferenceVelocity();
            Vector3 error = guidancePosition - goal;
            Vector2 planarError = new Vector2(error.x, error.z);
            Vector2 planarVelocity = new Vector2(guidanceVelocity.x, guidanceVelocity.z);
            Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;

            float upDot = Vector3.Dot(transform.up, Vector3.up);
            float goalClosureRate = error.sqrMagnitude > 0.0001f
                ? -Vector3.Dot(guidanceVelocity, error.normalized)
                : 0f;
            float horizontalClosureRate = planarError.sqrMagnitude > 0.0001f
                ? -Vector2.Dot(planarVelocity, planarError.normalized)
                : 0f;

            return new RewardTerms
            {
                distance3D = error.magnitude,
                planarDistance = planarError.magnitude,
                verticalError = error.y,
                speed = guidanceVelocity.magnitude,
                planarSpeed = planarVelocity.magnitude,
                verticalSpeed = guidanceVelocity.y,
                goalClosureRate = goalClosureRate,
                horizontalClosureRate = horizontalClosureRate,
                upDot = upDot,
                upright01 = Mathf.Clamp01((upDot + 1f) * 0.5f),
                angularRateDegS = localAngularVelocity.magnitude,
                yawRateDegS = localAngularVelocity.y,
                yawErrorDeg = Mathf.Abs(SignedHeadingErrorDeg()),
                controlEffort = Mean(throttle) +
                                0.05f * MeanAbs(gimbal) +
                                0.02f * MeanAbs(finAngles) +
                                0.10f * Mean(rcsValveStates)
            };
        }

        /// <summary>
        /// Measures and writes one telemetry row for the current simulation step.
        /// </summary>
        void LogTelemetry(bool forceStepWrite = false)
        {
            if (!TelemetryLogger.Instance) return;

            Vector3 effVel = rb.linearVelocity - wind;
            Vector3 targetPosition = GetAnalysisTargetPosition();
            Vector3 guidancePosition = ScenarioReferenceLocalPosition();
            Vector3 guidanceVelocity = ScenarioReferenceVelocity();
            Vector3 error = guidancePosition - targetPosition;
            Vector2 horizontalError = new Vector2(error.x, error.z);
            Vector2 horizontalVelocity = new Vector2(guidanceVelocity.x, guidanceVelocity.z);
            Vector2 horizontalTargetDir = horizontalError.sqrMagnitude > 0.0001f ? -horizontalError.normalized : Vector2.zero;
            Vector2 horizontalVelocityDir = horizontalVelocity.sqrMagnitude > 0.0001f ? horizontalVelocity.normalized : Vector2.zero;
            Vector2 meanGimbal = MeanVector(gimbal);
            float targetBearing = BearingDeg(horizontalTargetDir);
            float velocityBearing = BearingDeg(horizontalVelocityDir);
            float gimbalBearing = BearingDeg(meanGimbal);
            float goalAlignment = horizontalTargetDir.sqrMagnitude > 0f && horizontalVelocityDir.sqrMagnitude > 0f
                ? Vector2.Dot(horizontalVelocityDir, horizontalTargetDir)
                : 0f;
            Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;
            Vector3 euler = transform.localEulerAngles;
            float upDot = Vector3.Dot(transform.up, Vector3.up);
            float startFuel = Mathf.Max(cfg.startFuelMass, 1f);
            float goalClosureRate = error.sqrMagnitude > 0.0001f
                ? -Vector3.Dot(guidanceVelocity, error.normalized)
                : 0f;
            bool isHoverTracking = envConfig.scenario == ScenarioType.HoverTracking;
            float hoverTrackSettleRadius = isHoverTracking
                ? Mathf.Max(
                    envConfig.GetTrainingObjective(ScenarioType.HoverTracking)
                        .terminations.trackingCaptureRadiusM.At(_objectiveDifficulty01),
                    0.01f)
                : 0f;
            bool hoverTrackHoverPhase = isHoverTracking && horizontalError.magnitude <= hoverTrackSettleRadius;
            bool hoverTrackReady = isHoverTracking && IsHoverTrackHoverReady();
            float horizontalClosureRate = horizontalError.sqrMagnitude > 0.0001f
                ? -Vector2.Dot(horizontalVelocity, horizontalError.normalized)
                : 0f;
            bool isChopstickLanding = envConfig.scenario == ScenarioType.ChopstickLanding;
            bool isLegLanding = envConfig.scenario == ScenarioType.LegLanding;
            float directionEfficiency01 = horizontalVelocity.magnitude > 0.1f && horizontalError.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01((Vector2.Dot(horizontalVelocity.normalized, horizontalTargetDir) + 1f) * 0.5f)
                : 0.5f;
            float segmentDistance = Mathf.Max(_hoverTrackSegmentStartDistance, hoverTrackSettleRadius);
            float travelProgress01 = isHoverTracking
                ? Mathf.Clamp01(1f - horizontalError.magnitude / Mathf.Max(segmentDistance, 0.001f))
                : 0f;
            float travelProgressRate = isHoverTracking
                ? horizontalClosureRate / Mathf.Max(segmentDistance, 0.001f)
                : 0f;
            float settleQuality01 = isHoverTracking
                ? HoverTrackSettleQuality(horizontalError.magnitude, error.y, horizontalVelocity.magnitude,
                    guidanceVelocity.y, Vector3.Angle(transform.up, Vector3.up), localAngularVelocity.magnitude)
                : 0f;

            var row = new TelemetryRow
            {
                areaIndex = _areaIndex,
                episode   = _episode,
                step      = _step,

                obs_relPos       = (targetPosition - guidancePosition) / 100f,
                obs_vel          = guidanceVelocity / 50f,
                obs_angVel       = rb.angularVelocity / 10f,
                obs_up           = transform.up,
                obs_fuelFrac     = fuel / Mathf.Max(cfg.startFuelMass, 1f),
                obs_altNorm      = guidancePosition.y / 250f,
                obs_aoaDeg       = aoaDeg / 90f,
                obs_dynPressNorm = Mathf.Clamp01(q / 5000f),
                obs_windLocal    = transform.InverseTransformDirection(wind) /
                                   Mathf.Max(envConfig.windSpeed, 1f),

                act_throttle = throttle,
                act_gimbal   = gimbal,
                act_fins     = finAngles,

                sen = _sensors,

                phys_speed       = effVel.magnitude,
                phys_aoaDeg      = aoaDeg,
                phys_dynPressure = q,
                phys_altitude    = guidancePosition.y,
                phys_fuelKg      = fuel,

                goal_distance3D       = error.magnitude,
                goal_planarDistance   = horizontalError.magnitude,
                goal_verticalError    = error.y,
                track_phaseHover01    = hoverTrackHoverPhase ? 1f : 0f,
                track_hoverReady01    = hoverTrackReady ? 1f : 0f,
                track_stableTime      = isHoverTracking ? _hoverTrackStableTime : 0f,
                track_targetReached01 = _hoverTrackTargetReachedThisStep ? 1f : 0f,
                track_settleRadius    = isHoverTracking ? hoverTrackSettleRadius : 0f,
                track_curriculumProgress = isHoverTracking ? _objectiveDifficulty01 : 0f,
                track_segmentStartDistance = isHoverTracking ? _hoverTrackSegmentStartDistance : 0f,
                track_segmentElapsedTime = isHoverTracking ? _hoverTrackSegmentElapsedTime : 0f,
                track_travelProgress01 = travelProgress01,
                track_travelProgressRate = travelProgressRate,
                track_directionEfficiency01 = isHoverTracking ? directionEfficiency01 : 0f,
                track_settleQuality01 = settleQuality01,
                chopstick_platformRequired01 = isChopstickLanding &&
                    envConfig.GetTrainingObjective(ScenarioType.ChopstickLanding)
                        .terminations.chopstickRequireStablePlatform ? 1f : 0f,
                chopstick_platformInsideCapture01 = isChopstickLanding && _landingPlatformInsideCapture ? 1f : 0f,
                chopstick_platformStable01 = isChopstickLanding && _landingPlatformStable ? 1f : 0f,
                chopstick_platformStableTime = isChopstickLanding ? _landingPlatformStableTime : 0f,
                chopstick_platformHalfSize = isChopstickLanding ? ActiveLandingProfile.platformHalfSize : 0f,
                leg_touchdownStarted01 = isLegLanding && _legLanding.TouchdownStarted ? 1f : 0f,
                leg_firstContactEvent01 = isLegLanding && _legLanding.FirstContactThisStep ? 1f : 0f,
                leg_feetOnPad = isLegLanding ? LegLandingContactEvaluator.CountFeet(_legLanding.FootMask) : 0f,
                leg_foot1OnPad01 = isLegLanding && IsLandingFootOnPad(0) ? 1f : 0f,
                leg_foot2OnPad01 = isLegLanding && IsLandingFootOnPad(1) ? 1f : 0f,
                leg_foot3OnPad01 = isLegLanding && IsLandingFootOnPad(2) ? 1f : 0f,
                leg_foot4OnPad01 = isLegLanding && IsLandingFootOnPad(3) ? 1f : 0f,
                leg_footOutsidePad01 = isLegLanding && _legLanding.FootOutsidePad ? 1f : 0f,
                leg_structuralStrike01 = isLegLanding && _legLanding.StructuralStrike ? 1f : 0f,
                leg_stable01 = isLegLanding && _legLanding.Stable ? 1f : 0f,
                leg_stableTime = isLegLanding ? _legLanding.StableTime : 0f,
                leg_firstContactSpeed = isLegLanding ? _legLanding.FirstContactSpeed : 0f,
                leg_firstContactVerticalSpeed = isLegLanding ? _legLanding.FirstContactVerticalSpeed : 0f,
                leg_firstContactHorizontalSpeed = isLegLanding ? _legLanding.FirstContactHorizontalSpeed : 0f,
                leg_firstContactTiltDeg = isLegLanding ? _legLanding.FirstContactTiltDeg : 0f,
                leg_firstContactAngularRateDegS = isLegLanding ? _legLanding.FirstContactAngularRateDegS : 0f,
                leg_maxContactImpulseNs = isLegLanding ? _legLanding.MaximumContactImpulseNs : 0f,
                leg_maxReboundHeightM = isLegLanding ? _legLanding.MaximumReboundHeightM : 0f,
                state_altitude        = guidancePosition.y,
                nav_targetBearingDeg       = targetBearing,
                nav_velocityBearingDeg     = velocityBearing,
                nav_velocityTargetErrorDeg = SignedBearingError(velocityBearing, targetBearing),
                nav_goalAlignment          = goalAlignment,
                nav_gimbalBearingDeg       = gimbalBearing,
                nav_gimbalTargetErrorDeg   = SignedBearingError(gimbalBearing, targetBearing),
                att_tiltDeg           = Vector3.Angle(transform.up, Vector3.up),
                att_uprightness       = upDot,
                att_rollDeg           = NormalizeAngle(euler.z),
                att_pitchDeg          = NormalizeAngle(euler.x),
                att_angularRateDegS   = localAngularVelocity.magnitude,
                att_tiltRateDegS      = new Vector2(localAngularVelocity.x, localAngularVelocity.z).magnitude,
                att_pitchRateDegS     = localAngularVelocity.x,
                att_yawRateDegS       = localAngularVelocity.y,
                att_rollRateDegS      = localAngularVelocity.z,
                vel_speed3D           = guidanceVelocity.magnitude,
                vel_planarSpeed       = horizontalVelocity.magnitude,
                vel_verticalSpeed     = guidanceVelocity.y,
                vel_goalClosureRate   = goalClosureRate,
                vel_horizontalClosureRate = horizontalClosureRate,
                ctrl_throttleMean     = Mean(throttle),
                ctrl_throttleMax      = Max(throttle),
                ctrl_gimbalMeanAbsDeg = MeanAbs(gimbal),
                ctrl_finMeanAbsDeg    = MeanAbs(finAngles),
                ctrl_rcsActiveFraction = Mean(rcsValveStates),
                ctrl_throttleSaturatedFraction = FractionAtOrAbove(throttle, 0.98f),
                ctrl_gimbalSaturatedFraction = FractionAtLimit(gimbal, cfg.maxGimbal, 0.98f),
                ctrl_finSaturatedFraction = FractionAtLimit(finAngles, cfg.maxFinAngle, 0.98f),
                ctrl_engineRestartEvents = _engineRestartsThisStep,
                ctrl_engineRestartCount = _episodeEngineRestartCount,
                rcs_propellantKg      = rcsPropellant,
                rcs_propellantFraction = cfg.rcsPropellantMass > 0f
                    ? rcsPropellant / cfg.rcsPropellantMass
                    : 0f,
                fuel_fraction         = fuel / startFuel,
                fuel_usedKg           = Mathf.Max(0f, startFuel - fuel),
                load_gForce           = _sensors.GMagnitude,
                load_angularAccelDegS2 = _sensors.AngularAccel.magnitude * Mathf.Rad2Deg,
                env_windSpeed         = wind.magnitude,
                env_windPlanarSpeed   = new Vector2(wind.x, wind.z).magnitude,
                env_windAlignment     = effVel.sqrMagnitude > 0.0001f && wind.sqrMagnitude > 0.0001f
                    ? Vector3.Dot(effVel.normalized, wind.normalized)
                    : 0f,

                stepReward = _stepReward,
                rewardContributions = _rewardContributions
            };

            TelemetryLogger.Instance.Log(row, forceStepWrite);
        }

        /// <summary>
        /// Freezes the randomized episode start state for paired-seed analysis.
        /// Aggregate episode means cannot reconstruct these values later, so
        /// they are stored explicitly in every episode summary row.
        /// </summary>
        void CaptureEpisodeInitialTelemetry()
        {
            if (envConfig == null)
                return;

            RewardTerms terms = MeasureRewardTerms(
                ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig));
            _episodeInitialPlanarDistance = terms.planarDistance;
            _episodeInitialYawErrorDeg = terms.yawErrorDeg;
            _episodeInitialSpeed = terms.speed;
            _episodeInitialVerticalSpeed = terms.verticalSpeed;
            _episodeInitialHorizontalSpeed = terms.planarSpeed;
            _episodeInitialTiltDeg = Mathf.Acos(Mathf.Clamp(terms.upDot, -1f, 1f)) * Mathf.Rad2Deg;
            _episodeInitialAngularRateDegS = terms.angularRateDegS;
            _episodeInitialFuelKg = fuel;
            _episodeInitialVehicleMassKg = Mathf.Max(1f, cfg.dryMass + fuel + rcsPropellant);

            // These describe available authority at the episode's initial mass;
            // none is a commanded or rewarded throttle target. Independent
            // engines make one-engine minimum TWR different from all-engine
            // minimum-throttle TWR, so telemetry records both explicitly.
            ThrustAuthoritySnapshot authority = ThrustAuthorityMetrics.Calculate(
                _episodeInitialVehicleMassKg,
                Physics.gravity.y,
                cfg.activeEngineCount,
                cfg.maxThrust,
                cfg.minThrottle,
                cfg.independentEngines);
            _episodeMinimumCommandableNonzeroThrustToWeight = authority.MinimumCommandableNonzeroTwr;
            _episodeAllEnginesMinimumThrustToWeight = authority.AllActiveEnginesMinimumThrottleTwr;
            _episodeAllEnginesMaximumThrustToWeight = authority.AllActiveEnginesMaximumTwr;
        }

        /// <summary>
        /// Returns the scenario goal position used for reward analysis and telemetry.
        /// </summary>
        Vector3 GetAnalysisTargetPosition()
        {
            return ScenarioProfile.GoalPosition(envConfig.scenario, targetPad, envConfig);
        }

        /// <summary>
        /// Averages telemetry samples and emits zero when no samples exist so CSV fields stay numeric.
        /// </summary>
        static float Mean(float[] values)
        {
            if (values == null || values.Length == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum / values.Length;
        }

        /// <summary>
        /// Finds the peak telemetry sample and emits zero when no samples exist so CSV fields stay numeric.
        /// </summary>
        static float Max(float[] values)
        {
            if (values == null || values.Length == 0) return 0f;

            float max = values[0];
            for (int i = 1; i < values.Length; i++) max = Mathf.Max(max, values[i]);
            return max;
        }

        /// <summary>
        /// Normalizes an angle in degrees into the -180..180 range.
        /// </summary>
        static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        /// <summary>
        /// Converts an XZ-plane vector into a compass-style bearing in degrees.
        /// </summary>
        static float BearingDeg(Vector2 value)
        {
            if (value.sqrMagnitude < 0.0001f) return 0f;

            // XZ-plane bearing: 0 = north/+Z, 90 = east/+X, -90 = west/-X.
            return NormalizeAngle(Mathf.Atan2(value.x, value.y) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Returns the shortest signed angular error from a bearing to a target bearing.
        /// </summary>
        static float SignedBearingError(float bearingDeg, float targetBearingDeg)
        {
            return NormalizeAngle(targetBearingDeg - bearingDeg);
        }

        /// <summary>
        /// Returns the mean absolute scalar value, or zero for missing data.
        /// </summary>
        static float MeanAbs(float[] values)
        {
            if (values == null || values.Length == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < values.Length; i++) sum += Mathf.Abs(values[i]);
            return sum / values.Length;
        }

        /// <summary>
        /// Returns the mean absolute two-axis command magnitude across vectors.
        /// </summary>
        static float MeanAbs(Vector2[] values)
        {
            if (values == null || values.Length == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
                sum += (Mathf.Abs(values[i].x) + Mathf.Abs(values[i].y)) * 0.5f;

            return sum / values.Length;
        }

        /// <summary>Returns the fraction of scalar actuator slots at or above a normalized threshold.</summary>
        static float FractionAtOrAbove(float[] values, float threshold)
        {
            if (values == null || values.Length == 0) return 0f;

            int saturated = 0;
            for (int i = 0; i < values.Length; i++)
                if (values[i] >= threshold)
                    saturated++;
            return saturated / (float)values.Length;
        }

        /// <summary>Returns the fraction of scalar signed actuators operating near either configured limit.</summary>
        static float FractionAtLimit(float[] values, float limit, float threshold01)
        {
            if (values == null || values.Length == 0 || limit <= 0f) return 0f;

            int saturated = 0;
            float threshold = Mathf.Abs(limit) * Mathf.Clamp01(threshold01);
            for (int i = 0; i < values.Length; i++)
                if (Mathf.Abs(values[i]) >= threshold)
                    saturated++;
            return saturated / (float)values.Length;
        }

        /// <summary>Returns the fraction of two-axis actuators touching either axis limit.</summary>
        static float FractionAtLimit(Vector2[] values, float limit, float threshold01)
        {
            if (values == null || values.Length == 0 || limit <= 0f) return 0f;

            int saturated = 0;
            float threshold = Mathf.Abs(limit) * Mathf.Clamp01(threshold01);
            for (int i = 0; i < values.Length; i++)
                if (Mathf.Abs(values[i].x) >= threshold || Mathf.Abs(values[i].y) >= threshold)
                    saturated++;
            return saturated / (float)values.Length;
        }

        /// <summary>
        /// Returns the arithmetic mean vector, or zero for missing data.
        /// </summary>
        static Vector2 MeanVector(Vector2[] values)
        {
            if (values == null || values.Length == 0) return Vector2.zero;

            Vector2 sum = Vector2.zero;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum / values.Length;
        }

    }
}

// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Components/FinComponent.cs
// Purpose: Positions grid-fin objects around the body, stores their physical
// dimensions and limits, and applies the deflection angles produced by the agent.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace RocketSim
{
    public class FinComponent : MonoBehaviour
    {
        [Header("Geometry")]
        public FinLayout layout = FinLayout.FourFins_Plus;
        
        [Range(0.2f, 4f)]  public float finWidthX = RocketPartsConfig.DefaultFinRadialLengthM;
        [Range(0.2f, 4f)]  public float finWidthZ = RocketPartsConfig.DefaultFinTangentialWidthM;
        [Range(0.1f, 1f)]  public float finThickness = RocketPartsConfig.DefaultFinThicknessM;

        [Header("Actuator Limits")]
        [Range(5f,  60f)]  public float maxFinAngle = 35f;
        [Range(10f, 200f)] public float finSlewRate = 50f;

        [Header("Aero")]
        [Range(0.1f, 3f)]  public float liftScale = RocketPartsConfig.DefaultGridFinLiftScale;
        
        public float A_fin => finWidthX * finWidthZ;
        public int FinCount => layout switch
        {
            FinLayout.ThreeFins_120 => 3,
            _ => 4 
        };

        Quaternion[] _neutralRotations = new Quaternion[0];

        /// <summary>Copies fin settings from a session and updates fin geometry.</summary>
        public void ApplyConfiguration(RocketPartsConfig config, float bodyRadius, float bodyHeight)
        {
            if (config == null) return;

            gameObject.SetActive(config.finsEnabled);
            layout = config.finLayout;
            finWidthX = config.finWidthX;
            finWidthZ = config.finWidthZ;
            finThickness = config.finThickness;
            maxFinAngle = config.maxFinAngle;
            finSlewRate = config.finSlewRate;
            liftScale = config.liftScale;
            transform.localPosition = new Vector3(0f, bodyHeight - 1.2f, 0f);
            if (config.finsEnabled)
                ApplyLayout(bodyRadius);
        }
        
        /// <summary>
        /// Enables the fin children required by the selected layout, scales and
        /// positions them around the body, and remembers their neutral rotations
        /// for later deflection commands.
        /// </summary>
        public void ApplyLayout(float bodyRadius = 1.83f)
        {
            // Define the angles for the fins based on the layout
            float[] angles;
            switch (layout)
            {
                case FinLayout.ThreeFins_120:
                    angles = new[] { 0f, 120f, 240f }; // 3 fins, 120 degrees apart
                    break;
                case FinLayout.FourFins_X:
                    angles = new[] { 0f, 60f, 180f, 240f }; // 4 fins, 60 degrees apart between a pair and 120 degrees between pairs
                    break;
                case FinLayout.FourFins_Plus:
                default:
                    angles = new[] { 0f, 90f, 180f, 270f }; // 4 fins, 90 degrees apart
                    break;
            }

            // Apply positions and rotations
            if (_neutralRotations.Length != transform.childCount)
                _neutralRotations = new Quaternion[transform.childCount];

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform fin = transform.GetChild(i);

                if (i < angles.Length)
                {
                    fin.gameObject.SetActive(true);
                    fin.localScale = new Vector3(finWidthX, finThickness, finWidthZ);

                    float angleRad = angles[i] * Mathf.Deg2Rad;

                    // Distance from rocket center. 
                    float dist = bodyRadius + (finWidthX / 2f);
                    
                    float x = Mathf.Cos(angleRad) * dist;
                    float z = Mathf.Sin(angleRad) * dist;

                    fin.localPosition = new Vector3(x, 0f, z);
                    
                    // Rotate the fin so its forward/right axis points outward correctly
                    fin.localRotation = Quaternion.Euler(0f, -angles[i], 0f);
                    _neutralRotations[i] = fin.localRotation;
                }
                else
                {
                    // Disable extra fins
                    fin.gameObject.SetActive(false); 
                }
            }
        }

        /// <summary>
        /// Rotates each active fin away from its cached neutral orientation,
        /// clamping the requested angles to the configured actuator limit.
        /// </summary>
        public void ApplyDeflections(float[] deflectionsDeg)
        {
            if (deflectionsDeg == null) return;
            if (_neutralRotations.Length != transform.childCount)
                ApplyLayout();

            int activeFinIndex = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform fin = transform.GetChild(i);
                if (!fin.gameObject.activeSelf) continue;
                if (activeFinIndex >= deflectionsDeg.Length) break;

                float angle = Mathf.Clamp(deflectionsDeg[activeFinIndex], -maxFinAngle, maxFinAngle);
                fin.localRotation = _neutralRotations[i] * Quaternion.Euler(angle, 0f, 0f);
                activeFinIndex++;
            }
        }

        /// <summary>
        /// Applies serialized fin layout and dimensions when the component enters the scene.
        /// </summary>
        void Awake() => ApplyLayout();
    }
}

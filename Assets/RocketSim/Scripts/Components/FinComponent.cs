using System.Diagnostics;
using UnityEngine;

namespace RocketSim
{
    // Defines the actual physical arrangement of the fins
    public enum FinLayout
    {
        ThreeFins_120,   // 3 fins, 120 degrees apart
        FourFins_Plus,   // 4 fins, 90 degrees apart (+ shape)
        FourFins_X       // 4 fins, 90 degrees apart, rotated 45 degrees (X shape)
    }

    public class FinComponent : MonoBehaviour
    {
        [Header("Geometry — written by RocketAssembly.ApplyPartsConfig")]
        public FinLayout layout = FinLayout.FourFins_Plus;
        
        [Range(0.2f, 4f)]  public float finWidthX  = 1.73f;
        [Range(0.2f, 4f)]  public float finWidthZ  = 1.73f;
        [Range(0.2f, 1f)]  public float finHeight  = 0.3f;

        [Header("Actuator Limits")]
        [Range(5f,  60f)]  public float maxFinAngle = 35f;
        [Range(10f, 200f)] public float finSlewRate = 50f;

        [Header("Aero")]
        [Range(0.5f, 3f)]  public float liftScale = 1.0f;
        
        public float A_fin => finWidthX * finWidthZ;
        public int FinCount => layout switch
        {
            FinLayout.ThreeFins_120 => 3,
            _ => 4 
        };

        Quaternion[] _neutralRotations = new Quaternion[0];
        
        // Scale every child mesh and position them based on the selected layout
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
                    fin.localScale = new Vector3(finWidthX, finHeight, finWidthZ);

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

        void Awake() => ApplyLayout();
    }
}

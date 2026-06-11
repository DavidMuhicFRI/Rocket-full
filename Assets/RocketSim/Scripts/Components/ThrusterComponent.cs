using UnityEngine;

namespace RocketSim
{
    // Fields are overwritten by RocketAssembly.ApplyPartsConfig().
    public enum EngineLayout
    {
        Single,
        Triple,
        Octaweb
    }
    
    public class ThrusterComponent : MonoBehaviour
    {
        
        
        [Header("Engine Specs")]
        public EngineLayout layout = EngineLayout.Single;
        public bool independentEngines = true;
        [Range(50000f, 1200000f)]   public float maxThrustPerEngine = 845000f;
        [Range(0.15f, 0.80f)]       public float minThrottle        = 0.39f;
        [Range(0.2f, 1f)]           public float engineSpacing      = 0.8f;
        [Range(200f,  450f)]        public float specificImpulse    = 282f;

        [Header("Actuator Limits")]
        [Range(0.5f, 20f)] public float throttleSpoolRate = 5f;
        [Range(5f,   90f)] public float gimbalSlewRate    = 30f;
        [Range(1f,   15f)] public float maxGimbalAngle    = 7f;

        // ── Computed totals ───────────────────────────────────────────────────
        public int EngineCount => layout switch
        {
            EngineLayout.Single => 1,
            EngineLayout.Triple => 3,
            EngineLayout.Octaweb => 9,
            _ => 1
        };
        public int IndependentEngineCount => independentEngines ? EngineCount : 1;
        public float BurnRate => maxThrustPerEngine / (specificImpulse * 9.80665f);
        
        public void ApplyLayout(float bodyRadius)
        {
            float spacing = bodyRadius * engineSpacing;
            switch (layout)
            {
                case EngineLayout.Single: ApplyLayoutSingle(); break;
                case EngineLayout.Triple: ApplyLayoutTriple(spacing); break;
                case EngineLayout.Octaweb: ApplyLayoutOctaweb(spacing); break;
            }
        }

        void ApplyLayoutSingle()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform engine = transform.GetChild(i);
                if (i == 0) {
                    engine.transform.localPosition = Vector3.zero;
                    engine.gameObject.SetActive(true);
                } else {
                    engine.gameObject.SetActive(false);
                }
            }
        }
        
        void ApplyLayoutTriple(float spacing)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform engine = transform.GetChild(i);
                if (i < 3) {
                    float angle = i * 120f * Mathf.Deg2Rad;
                    engine.transform.localPosition = new Vector3(Mathf.Cos(angle) * spacing, 0, Mathf.Sin(angle) * spacing);
                    engine.gameObject.SetActive(true);
                } else {
                    engine.gameObject.SetActive(false);
                }
            }
        }

        void ApplyLayoutOctaweb(float spacing)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform engine = transform.GetChild(i);
                if (i == 0)
                {
                    engine.transform.localPosition = Vector3.zero;
                    engine.gameObject.SetActive(true);
                }
                else if (i < 9)                 {
                    float angle = (i - 1) * 45f * Mathf.Deg2Rad;
                    engine.transform.localPosition = new Vector3(Mathf.Cos(angle) * spacing, 0, Mathf.Sin(angle) * spacing);
                    engine.gameObject.SetActive(true);
                }
                else
                {
                    engine.gameObject.SetActive(false);
                }
            }
        }
    }
}
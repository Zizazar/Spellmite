using UnityEngine;

namespace Spellmite
{
    // Procedural spider legs for the parasite: built from primitives at runtime, 2-bone IK, tripod gait on any surface.
    [DefaultExecutionOrder(-8)]
    public class ParasiteLegs : MonoBehaviour
    {
        [SerializeField] private ParasiteBody parasite;
        [SerializeField, Tooltip("Legs are parented here so they hide with the body")] private Transform legRoot;
        [SerializeField] private Material legMaterial;
        [SerializeField] private LayerMask surfaceMask = 1;

        [Header("Shape")]
        [SerializeField] private int legPairs = 3;
        [SerializeField] private float hipSpread = 0.12f;
        [SerializeField] private float footSpread = 0.38f;
        [SerializeField] private float bodyHeight = 0.2f;
        [SerializeField] private float upperLength = 0.24f;
        [SerializeField] private float lowerLength = 0.3f;
        [SerializeField] private float thickness = 0.045f;

        [Header("Gait")]
        [SerializeField] private float stepDistance = 0.22f;
        [SerializeField] private float stepDuration = 0.07f;
        [SerializeField] private float stepHeight = 0.1f;
        [SerializeField, Tooltip("Seconds of velocity to place feet ahead")] private float stepLead = 0.06f;

        private class Leg
        {
            public Vector3 HipLocal;
            public Vector3 FootLocal;   // rest foot position, local to the parasite
            public int Group;
            public Vector3 Foot;
            public Vector3 StepFrom;
            public Vector3 StepTo;
            public float StepT = 1f;
            public Transform Upper;
            public Transform Lower;
            public bool Stepping => StepT < 1f;
        }

        private Leg[] legs;

        private void Awake()
        {
            legs = new Leg[legPairs * 2];
            for (int pair = 0; pair < legPairs; pair++)
            {
                float t = legPairs > 1 ? pair / (legPairs - 1f) : 0.5f;
                float angle = Mathf.Lerp(40f, 140f, t) * Mathf.Deg2Rad; // front to back
                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? -1f : 1f;
                    var dir = new Vector3(Mathf.Sin(angle) * sign, 0f, Mathf.Cos(angle));
                    var leg = new Leg
                    {
                        HipLocal = dir * hipSpread,
                        FootLocal = dir * footSpread + Vector3.down * bodyHeight,
                        Group = (pair + side) % 2,
                        Upper = CreateSegment($"Leg{pair}{side}_Upper"),
                        Lower = CreateSegment($"Leg{pair}{side}_Lower"),
                    };
                    leg.Foot = transform.TransformPoint(leg.FootLocal);
                    legs[pair * 2 + side] = leg;
                }
            }
        }

        private Transform CreateSegment(string segmentName)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = segmentName;
            Destroy(go.GetComponent<Collider>());
            if (legMaterial != null) go.GetComponent<Renderer>().sharedMaterial = legMaterial;
            go.transform.SetParent(legRoot != null ? legRoot : transform, false);
            return go.transform;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            Transform body = parasite.transform;
            Vector3 up = body.up;
            bool grounded = parasite.IsOnSurface;

            foreach (var leg in legs)
            {
                Vector3 hip = body.TransformPoint(leg.HipLocal);

                if (grounded)
                {
                    UpdateGroundedFoot(leg, body, up, dt);
                }
                else
                {
                    // Airborne / riding a host: legs splay forward and curl in
                    Vector3 local = leg.FootLocal * 0.75f + Vector3.forward * 0.08f + Vector3.up * 0.05f;
                    leg.Foot = Vector3.Lerp(leg.Foot, body.TransformPoint(local), 1f - Mathf.Exp(-25f * dt));
                    leg.StepT = 1f;
                }

                Vector3 outward = (hip - body.position).normalized;
                PlaceSegments(leg, hip, leg.Foot, up + outward * 0.6f);
            }
        }

        private void UpdateGroundedFoot(Leg leg, Transform body, Vector3 up, float dt)
        {
            Vector3 home = body.TransformPoint(leg.FootLocal) + parasite.Velocity * stepLead;
            if (Physics.Raycast(home + up * 0.3f, -up, out var hit, 0.6f, surfaceMask, QueryTriggerInteraction.Ignore))
                home = hit.point;

            if (leg.Stepping)
            {
                leg.StepT = Mathf.Min(1f, leg.StepT + dt / stepDuration);
                leg.StepTo = home; // keep tracking while the body moves
                leg.Foot = Vector3.Lerp(leg.StepFrom, leg.StepTo, leg.StepT) + up * (Mathf.Sin(leg.StepT * Mathf.PI) * stepHeight);
                return;
            }

            float distance = Vector3.Distance(leg.Foot, home);
            if (distance > stepDistance * 4f)
            {
                leg.Foot = home; // teleported / landed far away
            }
            else if (distance > stepDistance && !GroupStepping(1 - leg.Group))
            {
                leg.StepFrom = leg.Foot;
                leg.StepTo = home;
                leg.StepT = 0f;
            }
        }

        private bool GroupStepping(int group)
        {
            foreach (var leg in legs)
                if (leg.Group == group && leg.Stepping) return true;
            return false;
        }

        private void PlaceSegments(Leg leg, Vector3 hip, Vector3 foot, Vector3 pole)
        {
            Vector3 toFoot = foot - hip;
            float reach = upperLength + lowerLength - 0.001f;
            float distance = Mathf.Clamp(toFoot.magnitude, 0.01f, reach);
            Vector3 dir = toFoot.sqrMagnitude > 1e-6f ? toFoot.normalized : -parasite.transform.up;

            // Law of cosines: knee sits on the pole side of the hip-foot line
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir).normalized;
            Vector3 knee = hip + dir * along + bend * height;
            Vector3 end = hip + dir * distance;

            SetSegment(leg.Upper, hip, knee);
            SetSegment(leg.Lower, knee, end);
        }

        private void SetSegment(Transform segment, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            segment.position = (from + to) * 0.5f;
            if (length > 1e-5f) segment.rotation = Quaternion.FromToRotation(Vector3.up, delta / length);
            // Unity capsule is 2 units tall, 1 unit wide; keep world size independent of parent scale
            Vector3 parentScale = segment.parent.lossyScale;
            segment.localScale = new Vector3(thickness / parentScale.x, length * 0.5f / parentScale.y, thickness / parentScale.z);
        }
    }
}

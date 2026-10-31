using UnityEngine;

namespace Spellmite
{
    public static class TwoBoneIK
    {
        // Rotates upper and lower so that end reaches target, bending towards pole. Run after the Animator (LateUpdate).
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, float weight)
        {
            if (weight <= 0f) return;

            Quaternion upperStart = upper.rotation;
            Quaternion lowerStart = lower.rotation;

            Vector3 a = upper.position;
            float upperLength = Vector3.Distance(a, lower.position);
            float lowerLength = Vector3.Distance(lower.position, end.position);

            Vector3 toTarget = target - a;
            if (toTarget.sqrMagnitude < 1e-6f) return;
            float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + 1e-3f, upperLength + lowerLength - 1e-3f);
            Vector3 dir = toTarget.normalized;

            Vector3 bend = Vector3.ProjectOnPlane(pole - a, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(lower.position - a, dir);
            bend.Normalize();

            float cos = Mathf.Clamp((upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * upperLength * distance), -1f, 1f);
            float sin = Mathf.Sqrt(1f - cos * cos);
            Vector3 desiredMid = a + (dir * cos + bend * sin) * upperLength;

            upper.rotation = Quaternion.FromToRotation(lower.position - a, desiredMid - a) * upper.rotation;
            Vector3 desiredEnd = a + dir * distance;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, desiredEnd - lower.position) * lower.rotation;

            if (weight < 1f)
            {
                Quaternion upperSolved = upper.rotation;
                Quaternion lowerSolved = lower.rotation;
                upper.rotation = Quaternion.Slerp(upperStart, upperSolved, weight);
                lower.rotation = Quaternion.Slerp(lowerStart, lowerSolved, weight);
            }
        }
    }
}

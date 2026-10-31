using System.Collections.Generic;
using UnityEngine;

namespace Spellmite
{
    // A spawn pocket enemies walk out of but never into: EnemyBrain treats a move deeper into it as a wall,
    // ParasiteBody pushes the player back out.
    // The box is this object's unit cube scaled by its transform; forward (the gizmo line) points out of the pocket.
    public class EnemyPocket : MonoBehaviour
    {
        private static readonly List<EnemyPocket> All = new();

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        // Moving from 'from' along 'dir' would step into a pocket, or deeper into the one we're in
        public static bool Blocks(Vector3 from, Vector3 dir, float lookAhead)
        {
            dir.Normalize();
            foreach (var pocket in All)
            {
                // Sideways or outward is always fine, so a fresh spawn (or one knocked in) walks out
                if (Vector3.Dot(dir, pocket.transform.forward) > -0.1f) continue;
                if (pocket.Contains(from + dir * lookAhead, out _)) return true;
            }
            return false;
        }

        // For the player: outward direction scaled by how deep 'point' is, 0 at the opening, 1 at the back wall
        public static Vector3 PushAt(Vector3 point)
        {
            Vector3 push = Vector3.zero;
            foreach (var pocket in All)
            {
                if (!pocket.Contains(point, out Vector3 local)) continue;
                push += Vector3.ProjectOnPlane(pocket.transform.forward, Vector3.up).normalized * (0.5f - local.z);
            }
            return push;
        }

        private bool Contains(Vector3 point, out Vector3 local)
        {
            local = transform.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f && Mathf.Abs(local.z) <= 0.5f;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.5f);
        }
    }
}

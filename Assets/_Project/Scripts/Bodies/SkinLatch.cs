using System;
using UnityEngine;

namespace Spellmite
{
    // Glues this transform to a patch of a skinned mesh, so whatever rides it follows the animated skin, not one bone.
    // Three skin vertices around the spot are skinned on the CPU every frame and this transform keeps its baked pose
    // relative to the triangle they make. To move it: in Prefab Mode put this transform on the skin as shown
    // (Y = out of the skin, Z = towards the head), then context menu "Bake From Current Pose".
    [DefaultExecutionOrder(-15)] // after SeekerProceduralAnimator (-20) moves bones, before ParasiteBody (-10) reads the latch point
    public class SkinLatch : MonoBehaviour
    {
        [Serializable]
        private struct Influence
        {
            public Transform Bone;
            public float Weight;
            public Vector3 Point; // the vertex in the bone's space
        }

        [Serializable]
        private class Anchor
        {
            public Influence[] Influences;
        }

        [SerializeField] private SkinnedMeshRenderer skin;
        [SerializeField, Tooltip("How far apart the anchor vertices are: bigger rides over small bumps of the mesh")] private float patchRadius = 0.1f;
        [SerializeField, HideInInspector] private Anchor[] anchors;
        [SerializeField, HideInInspector] private Vector3 localPosition;
        [SerializeField, HideInInspector] private Quaternion localRotation = Quaternion.identity;

        private void LateUpdate()
        {
            if (anchors == null || anchors.Length != 3) return;
            Quaternion frame = Frame(Skin(anchors[0]), Skin(anchors[1]), Skin(anchors[2]), out Vector3 origin);
            transform.SetPositionAndRotation(origin + frame * localPosition, frame * localRotation);
        }

        private static Vector3 Skin(Anchor anchor)
        {
            Vector3 point = Vector3.zero;
            foreach (var influence in anchor.Influences)
                point += influence.Bone.TransformPoint(influence.Point) * influence.Weight;
            return point;
        }

        private static Quaternion Frame(Vector3 a, Vector3 b, Vector3 c, out Vector3 origin)
        {
            origin = (a + b + c) / 3f;
            return Quaternion.LookRotation(b - a, Vector3.Cross(b - a, c - a));
        }

#if UNITY_EDITOR
        [ContextMenu("Bake From Current Pose")]
        private void Bake()
        {
            Mesh mesh = skin.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;
            Transform[] bones = skin.bones;

            var skinMatrices = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) skinMatrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            var world = new Vector3[vertices.Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                BoneWeight w = weights[v];
                world[v] = skinMatrices[w.boneIndex0].MultiplyPoint3x4(vertices[v]) * w.weight0
                           + skinMatrices[w.boneIndex1].MultiplyPoint3x4(vertices[v]) * w.weight1
                           + skinMatrices[w.boneIndex2].MultiplyPoint3x4(vertices[v]) * w.weight2
                           + skinMatrices[w.boneIndex3].MultiplyPoint3x4(vertices[v]) * w.weight3;
            }

            // Only skin that mostly follows this transform's bone and its parents (the spine), so arm swings don't drag it
            var chain = new System.Collections.Generic.HashSet<Transform>();
            for (Transform t = transform.parent; t != null; t = t.parent) chain.Add(t);
            var torso = new bool[vertices.Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                BoneWeight w = weights[v];
                float sum = (chain.Contains(bones[w.boneIndex0]) ? w.weight0 : 0f) + (chain.Contains(bones[w.boneIndex1]) ? w.weight1 : 0f)
                            + (chain.Contains(bones[w.boneIndex2]) ? w.weight2 : 0f) + (chain.Contains(bones[w.boneIndex3]) ? w.weight3 : 0f);
                torso[v] = sum >= 0.75f;
            }

            // Three spread-out vertices around the spot under this transform, only from the skin below it
            Vector3 up = transform.up;
            var picked = new System.Collections.Generic.List<int>();
            int centerIndex = Nearest(world, torso, picked, transform.position, up);
            if (centerIndex < 0)
            {
                Debug.LogError("SkinLatch: no spine skin under the latch point", this);
                return;
            }
            Vector3 center = world[centerIndex];
            Vector3 side = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 side2 = Vector3.Cross(up, side);
            for (int k = 0; k < 3; k++)
            {
                float angle = (90f + 120f * k) * Mathf.Deg2Rad;
                Vector3 target = center + (side * Mathf.Cos(angle) + side2 * Mathf.Sin(angle)) * patchRadius;
                picked.Add(Nearest(world, torso, picked, target, up));
            }
            if (picked.Contains(-1))
            {
                Debug.LogError("SkinLatch: not enough spine skin under the latch point", this);
                return;
            }

            UnityEditor.Undo.RecordObject(this, "Bake Skin Latch");
            anchors = new Anchor[3];
            for (int k = 0; k < 3; k++)
            {
                BoneWeight w = weights[picked[k]];
                var list = new System.Collections.Generic.List<Influence>();
                void Add(int bone, float weight)
                {
                    if (weight > 0f) list.Add(new Influence { Bone = bones[bone], Weight = weight, Point = bindposes[bone].MultiplyPoint3x4(vertices[picked[k]]) });
                }
                Add(w.boneIndex0, w.weight0);
                Add(w.boneIndex1, w.weight1);
                Add(w.boneIndex2, w.weight2);
                Add(w.boneIndex3, w.weight3);
                anchors[k] = new Anchor { Influences = list.ToArray() };
            }

            Quaternion frame = Frame(Skin(anchors[0]), Skin(anchors[1]), Skin(anchors[2]), out Vector3 origin);
            localPosition = Quaternion.Inverse(frame) * (transform.position - origin);
            localRotation = Quaternion.Inverse(frame) * transform.rotation;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        // Duplicate vertices along UV seams sit on the same spot: picks are kept apart so the triangle never collapses
        private int Nearest(Vector3[] world, bool[] allowed, System.Collections.Generic.List<int> picked, Vector3 target, Vector3 up)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int v = 0; v < world.Length; v++)
            {
                if (!allowed[v] || Vector3.Dot(world[v] - transform.position, up) > 0f) continue;
                bool tooClose = false;
                foreach (int p in picked)
                    if ((world[p] - world[v]).sqrMagnitude < patchRadius * patchRadius * 0.25f) tooClose = true;
                if (tooClose) continue;
                float distance = (world[v] - target).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = v;
            }
            return best;
        }
#endif
    }
}

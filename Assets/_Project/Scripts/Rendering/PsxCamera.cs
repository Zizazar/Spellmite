using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Spellmite
{
    // PlayStation 1 look for this camera. The screen part (low resolution, 15-bit color, dithering) is drawn by
    // PsxRendererFeature; the geometry part (vertex jitter, affine textures) is done by Spellmite/PSX Lit materials,
    // which read the globals set here. Other cameras (Scene view, previews) render with all of it off.
    // In Play Mode and builds it also sets the URP render scale so the camera renders right at the PSX height.
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class PsxCamera : MonoBehaviour
    {
        private static readonly int SnapResolutionId = Shader.PropertyToID("_PsxSnapResolution");
        private static readonly int AffineId = Shader.PropertyToID("_PsxAffine");

        // The URP asset whose render scale is changed and its own value, put back when the camera is disabled
        // (also on leaving Play Mode, so the asset isn't left changed in the Editor)
        private static UniversalRenderPipelineAsset scaledAsset;
        private static float originalRenderScale;

        [Header("Screen")]
        [SerializeField, Tooltip("Internal render height in pixels (PS1: 240). Width follows the aspect ratio. 0 = full resolution")]
        [Range(0, 720)] private int verticalResolution = 240;
        [SerializeField, Tooltip("Bits per color channel (PS1: 5 = 15-bit color)")]
        [Range(1, 8)] private int colorBits = 5;
        [SerializeField, Tooltip("Strength of the PS1 4x4 ordered dither applied before color reduction")]
        [Range(0f, 1f)] private float dither = 1f;

        [Header("Geometry (PSX Lit materials)")]
        [SerializeField, Tooltip("Height of the screen grid vertices snap to. Same as Vertical Resolution = authentic, lower = more wobble. 0 = off")]
        [Range(0, 720)] private int vertexSnapHeight = 240;
        [SerializeField, Tooltip("0 = perspective-correct textures, 1 = PS1 affine texture warping")]
        [Range(0f, 1f)] private float affineMapping = 1f;

        private Camera cam;

        public int VerticalResolution { get => verticalResolution; set => verticalResolution = value; }
        public int ColorBits { get => colorBits; set => colorBits = value; }
        public float Dither { get => dither; set => dither = value; }
        public int VertexSnapHeight { get => vertexSnapHeight; set => vertexSnapHeight = value; }
        public float AffineMapping { get => affineMapping; set => affineMapping = value; }

        private void OnEnable()
        {
            cam = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            SetGeometryGlobals(Vector2.zero, 0f);
            RestoreRenderScale();
        }

        // Globals are shared by all cameras, so they are set right before each camera renders.
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
        {
            if (renderingCamera != cam)
            {
                if (!renderingCamera.TryGetComponent(out PsxCamera other) || !other.isActiveAndEnabled)
                    SetGeometryGlobals(Vector2.zero, 0f);
                return;
            }

            var snap = vertexSnapHeight > 0
                ? new Vector2(Mathf.Round(vertexSnapHeight * cam.aspect), vertexSnapHeight)
                : Vector2.zero;
            SetGeometryGlobals(snap, affineMapping);
            if (Application.isPlaying) ApplyRenderScale();
        }

        // The PSX pass takes one pixel per block of the full-size image anyway, so rendering at its size looks
        // the same and shades 13-20x fewer pixels at 1080p. Camera.pixelHeight is the window's, render scale doesn't change it.
        private void ApplyRenderScale()
        {
            var asset = UniversalRenderPipeline.asset;
            if (asset != scaledAsset)
            {
                RestoreRenderScale();
                if (asset == null) return;
                scaledAsset = asset;
                originalRenderScale = asset.renderScale;
            }

            // +0.5: URP truncates the scaled height, and a line short would make the PSX pass resample the image
            float scale = verticalResolution > 0
                ? Mathf.Clamp((verticalResolution + 0.5f) / cam.pixelHeight, 0.1f, 1f)
                : originalRenderScale;
            if (asset.renderScale != scale) asset.renderScale = scale;
        }

        private static void RestoreRenderScale()
        {
            if (scaledAsset != null) scaledAsset.renderScale = originalRenderScale;
            scaledAsset = null;
        }

        private static void SetGeometryGlobals(Vector2 snapResolution, float affine)
        {
            Shader.SetGlobalVector(SnapResolutionId, snapResolution);
            Shader.SetGlobalFloat(AffineId, affine);
        }
    }
}

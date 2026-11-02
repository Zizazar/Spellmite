using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Spellmite
{
    // Screen part of the PSX look: renders the camera image at a low resolution with reduced color depth and
    // dithering, then scales it back up without filtering. Runs only for game cameras that have an enabled
    // PsxCamera (so never in the Scene view); settings come from that component.
    public class PsxRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader shader;
        [SerializeField, Tooltip("After Post Processing keeps bloom/tonemapping at full resolution before the PSX reduction")]
        private RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;

        private Material material;
        private PsxPass pass;

        public override void Create()
        {
            pass = new PsxPass { renderPassEvent = passEvent, requiresIntermediateTexture = true };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            if (!renderingData.cameraData.camera.TryGetComponent(out PsxCamera settings) || !settings.isActiveAndEnabled) return;
            if (shader == null) return;

            if (material == null) material = CoreUtils.CreateEngineMaterial(shader);
            pass.Setup(material, settings);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        private class PsxPass : ScriptableRenderPass
        {
            private static readonly int ColorBitsId = Shader.PropertyToID("_PsxColorBits");
            private static readonly int DitherId = Shader.PropertyToID("_PsxDither");
            private static readonly Vector4 NoScaleBias = new(1f, 1f, 0f, 0f);

            private Material material;
            private PsxCamera settings;

            private class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            public void Setup(Material psxMaterial, PsxCamera psxSettings)
            {
                material = psxMaterial;
                settings = psxSettings;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (resourceData.isActiveTargetBackBuffer) return;

                var cameraColor = resourceData.activeColorTexture;
                var target = cameraData.cameraTargetDescriptor;
                int height = settings.VerticalResolution > 0 ? Mathf.Min(settings.VerticalResolution, target.height) : target.height;
                int width = Mathf.Max(1, Mathf.RoundToInt(height * (float)target.width / target.height));

                // sRGB storage in linear projects keeps the reduced color levels exact after the gamma round trip.
                var lowResDesc = new TextureDesc(width, height)
                {
                    format = QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
                    filterMode = FilterMode.Point,
                    name = "_PsxLowRes"
                };
                var lowRes = renderGraph.CreateTexture(lowResDesc);

                material.SetFloat(ColorBitsId, settings.ColorBits);
                material.SetFloat(DitherId, settings.Dither);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("PSX Color", out var data))
                {
                    data.source = cameraColor;
                    data.material = material;
                    builder.UseTexture(cameraColor);
                    builder.SetRenderAttachment(lowRes, 0);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, d.source, NoScaleBias, d.material, 0));
                }

                // The camera already renders at this size (PsxCamera's render scale): the low-res image becomes the
                // camera color, and URP's final blit scales it to the screen with point sampling (it's a Point texture).
                // Copied back into the camera color it would be scaled up bilinearly, as camera post-processing is off.
                if (width == target.width && height == target.height)
                {
                    resourceData.cameraColor = lowRes;
                    return;
                }

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("PSX Upscale", out var data))
                {
                    data.source = lowRes;
                    builder.UseTexture(lowRes);
                    builder.SetRenderAttachment(cameraColor, 0);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, d.source, NoScaleBias, 0f, false));
                }
            }
        }
    }
}

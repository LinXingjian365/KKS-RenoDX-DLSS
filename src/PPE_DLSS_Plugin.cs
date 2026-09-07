using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPE_DLSS
{
    [BepInPlugin("com.user.ppe_dlss", "KKS DLSS Upscaler", "2.1.0")]
    public class PPE_DLSS_Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static PPE_DLSS_Plugin Instance;

        public static ConfigEntry<bool> EnableDLSS;
        public static ConfigEntry<float> ScaleFactor;
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<bool> ShowUI;

        private DLSSComponent _dlssComponent;
        private float _nextRetry;
        private int _attemptCount;
        private string _status = "OFF";
        private bool _nativeInitBlocked;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            EnableDLSS = Config.Bind("General", "EnableDLSS", false, "Enable DLSS (default off, Ctrl+D to toggle)");
            ScaleFactor = Config.Bind("General", "ScaleFactor", 1.5f, new ConfigDescription("Upscale factor", new AcceptableValueRange<float>(1.2f, 3.0f)));
            ToggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.D, KeyCode.LeftControl), "Toggle DLSS");
            ShowUI = Config.Bind("General", "ShowUI", true, "Show status UI");

            Log.LogInfo("KKS DLSS Upscaler v2.1.0 loaded (native NGX experimental path). Default off, Ctrl+D to enable.");
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                if (EnableDLSS.Value)
                {
                    DisableDLSS();
                }
                else
                {
                    _nativeInitBlocked = false;
                    EnableDLSS.Value = true;
                    TryEnableDLSS();
                }
            }
            else if (EnableDLSS.Value && !_nativeInitBlocked && _dlssComponent == null && Time.unscaledTime >= _nextRetry)
            {
                TryEnableDLSS();
            }
        }

        private void TryEnableDLSS()
        {
            _attemptCount++;
            _nextRetry = Time.unscaledTime + 3f;
            Log.LogInfo($"Enabling DLSS (attempt {_attemptCount})...");
            Log.LogInfo($"Graphics={SystemInfo.graphicsDeviceType}, shaderLevel={SystemInfo.graphicsShaderLevel}, compute={SystemInfo.supportsComputeShaders}, driver={SystemInfo.graphicsDeviceVersion}");

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            {
                Log.LogError($"DLSS requires D3D11, current: {SystemInfo.graphicsDeviceType}");
                _status = "UNSUPPORTED_API";
                EnableDLSS.Value = false;
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                Camera[] cams = Camera.allCameras;
                float maxDepth = float.MinValue;
                foreach (var c in cams)
                {
                    if (c.depth > maxDepth && c.targetTexture == null)
                    {
                        maxDepth = c.depth;
                        cam = c;
                    }
                }
            }

            if (cam == null)
            {
                Log.LogError("Cannot find main camera");
                _status = "WAITING_FOR_CAMERA";
                return;
            }

            Log.LogInfo($"Attaching DLSS to camera: {cam.name} (depth={cam.depth})");
            _dlssComponent = cam.gameObject.AddComponent<DLSSComponent>();
            _status = "INITIALIZING";
        }

        private void DisableDLSS()
        {
            EnableDLSS.Value = false;
            _status = "OFF";
            if (_dlssComponent != null)
            {
                Destroy(_dlssComponent);
                _dlssComponent = null;
            }
            Log.LogInfo("DLSS disabled");
        }

        private void OnGUI()
        {
            if (!ShowUI.Value) return;
            string status = _dlssComponent != null && _dlssComponent.IsActive
                ? $"DLSS ON | {_dlssComponent.RenderWidth}x{_dlssComponent.RenderHeight} -> {Screen.width}x{Screen.height}"
                : $"DLSS {_status} (Ctrl+D to enable)";
            GUI.color = _dlssComponent != null && _dlssComponent.IsActive ? Color.green : Color.yellow;
            GUI.Label(new Rect(10, 10, 400, 20), status);
            GUI.color = Color.white;
        }

        public void NotifyInitFailed()
        {
            _nativeInitBlocked = true;
            Log.LogError("DLSS init failed; automatic retries stopped. Press Ctrl+D after changing the native bridge/runtime.");
            EnableDLSS.Value = false;
            _status = "NATIVE_NGX_UNAVAILABLE";
            if (_dlssComponent != null)
            {
                Destroy(_dlssComponent);
                _dlssComponent = null;
            }
        }
    }

    public class DLSSComponent : MonoBehaviour
    {
        private DLSSWrapper _dlss;
        private Camera _cam;
        private DLSSGuideCapture _guides;
        private bool _initialized;
        private float _lastFrameTime;
        private float _originalScale = 1f;

        public int RenderWidth => _dlss?.RenderWidth ?? 0;
        public int RenderHeight => _dlss?.RenderHeight ?? 0;
        public bool IsActive => _initialized && _dlss != null && _dlss.IsInitialized;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null)
            {
                PPE_DLSS_Plugin.Log.LogError("DLSSComponent requires Camera component");
                Destroy(this);
            }
        }

        private void OnEnable()
        {
            if (_cam != null)
            {
                _cam.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
                _guides = new DLSSGuideCapture(_cam);
                _guides.Install();
            }
            try
            {
                TryInitialize();
            }
            catch (Exception e)
            {
                PPE_DLSS_Plugin.Log.LogError($"DLSS init exception: {e.Message}");
                PPE_DLSS_Plugin.Instance?.NotifyInitFailed();
            }
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void TryInitialize()
        {
            if (_initialized) return;

            int outW = Screen.width;
            int outH = Screen.height;
            float scale = PPE_DLSS_Plugin.ScaleFactor.Value;
            int renderW = Mathf.RoundToInt(outW / scale);
            int renderH = Mathf.RoundToInt(outH / scale);

            if (renderW < 100 || renderH < 100)
            {
                PPE_DLSS_Plugin.Log.LogError($"Render resolution too small: {renderW}x{renderH}");
                PPE_DLSS_Plugin.Instance?.NotifyInitFailed();
                return;
            }

            PPE_DLSS_Plugin.Log.LogInfo($"DLSS init: {renderW}x{renderH} -> {outW}x{outH} (scale={scale})");

            // Lower render resolution via ScalableBufferManager
            _originalScale = ScalableBufferManager.widthScaleFactor;
            ScalableBufferManager.ResizeBuffers(1f / scale, 1f / scale);

            // Init DLSS with new API
            _dlss = new DLSSWrapper();
            if (!_dlss.Init(renderW, renderH, outW, outH))
            {
                PPE_DLSS_Plugin.Log.LogError("DLSS SDK init failed");
                PPE_DLSS_Plugin.Instance?.NotifyInitFailed();
                return;
            }

            _initialized = true;
            PPE_DLSS_Plugin.Log.LogInfo("DLSS init successful!");
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!_initialized || _dlss == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            try
            {
                float frameTime = (Time.realtimeSinceStartup - _lastFrameTime) * 1000f;
                _lastFrameTime = Time.realtimeSinceStartup;

                // Copy source to DLSS color input
                var colorRT = _dlss.GetColorTexture();
                if (colorRT != null)
                {
                    Graphics.Blit(source, colorRT);
                }

                // Unity exposes these camera resources after depthTextureMode is enabled.
                // They are the only native scene guides available without a ReShade bridge.
                Texture sceneDepth = _guides?.DepthTexture ?? Shader.GetGlobalTexture("_CameraDepthTexture");
                Texture sceneMotionVectors = _guides?.MotionTexture ?? Shader.GetGlobalTexture("_CameraMotionVectorsTexture");

                if (sceneDepth == null || sceneMotionVectors == null)
                    PPE_DLSS_Plugin.Log.LogWarning("Native DLSS guide missing: " +
                        (sceneDepth == null ? "depth " : "") +
                        (sceneMotionVectors == null ? "motion-vectors" : ""));

                // Execute DLSS with the live Unity guide resources.
                bool success = _dlss.Evaluate(frameTime, sceneDepth, sceneMotionVectors);

                if (success)
                {
                    var outputRT = _dlss.GetOutputTexture();
                    if (outputRT != null)
                    {
                        Graphics.Blit(outputRT, destination);
                    }
                    else
                    {
                        Graphics.Blit(source, destination);
                    }
                }
                else
                {
                    Graphics.Blit(source, destination);
                }
            }
            catch (Exception e)
            {
                PPE_DLSS_Plugin.Log.LogError($"DLSS render exception: {e.Message}");
                Graphics.Blit(source, destination);
            }
        }

        private void Cleanup()
        {
            try { ScalableBufferManager.ResizeBuffers(_originalScale, _originalScale); } catch { }

            try { _guides?.Dispose(); } catch { }
            _guides = null;

            _initialized = false;
            _dlss?.Dispose();
            _dlss = null;
        }

        private void OnDestroy()
        {
            Cleanup();
        }
    }

    // Captures Unity Built-in Forward attachments before post-processing. This is
    // an input probe: it does not manufacture motion vectors when KKS shaders omit them.
    internal sealed class DLSSGuideCapture : IDisposable
    {
        private readonly Camera _camera;
        private CommandBuffer _commandBuffer;
        private RenderTexture _depthTexture;
        private RenderTexture _motionTexture;

        public Texture DepthTexture => _depthTexture;
        public Texture MotionTexture => _motionTexture;

        public DLSSGuideCapture(Camera camera)
        {
            _camera = camera;
        }

        public void Install()
        {
            if (_camera == null || _commandBuffer != null) return;

            int width = Mathf.Max(1, _camera.pixelWidth);
            int height = Mathf.Max(1, _camera.pixelHeight);
            _depthTexture = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat)
            {
                name = "KKS_DLSS_NativeDepth",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _motionTexture = new RenderTexture(width, height, 0, RenderTextureFormat.RGHalf)
            {
                name = "KKS_DLSS_NativeMotionVectors",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _depthTexture.Create();
            _motionTexture.Create();

            _commandBuffer = new CommandBuffer { name = "KKS DLSS native guide capture" };
            _commandBuffer.Blit(BuiltinRenderTextureType.Depth, new RenderTargetIdentifier(_depthTexture));
            _commandBuffer.Blit(BuiltinRenderTextureType.MotionVectors, new RenderTargetIdentifier(_motionTexture));
            _camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _commandBuffer);
            PPE_DLSS_Plugin.Log.LogInfo($"Native guide capture installed: {width}x{height}");
        }

        public void Dispose()
        {
            if (_camera != null && _commandBuffer != null)
            {
                _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _commandBuffer);
            }
            _commandBuffer?.Release();
            _commandBuffer = null;
            if (_depthTexture != null) UnityEngine.Object.Destroy(_depthTexture);
            if (_motionTexture != null) UnityEngine.Object.Destroy(_motionTexture);
            _depthTexture = null;
            _motionTexture = null;
        }
    }
}

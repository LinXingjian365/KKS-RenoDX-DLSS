using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPE_DLSS
{
    [BepInPlugin("com.user.ppe_dlss", "KKS DLSS Upscaler", "2.1.1")]
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
        private float _nextToggleAllowed;
        private float _toggleTransitionUntil;

        private const float ToggleCooldownSeconds = 1.0f;
        private const float ToggleTransitionSeconds = 0.35f;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            EnableDLSS = Config.Bind("General", "EnableDLSS", false, "Enable DLSS (default off, Ctrl+D to toggle)");
            ScaleFactor = Config.Bind("General", "ScaleFactor", 1.5f, new ConfigDescription("Upscale factor", new AcceptableValueRange<float>(1.2f, 3.0f)));
            ToggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.D, KeyCode.LeftControl), "Toggle DLSS");
            ShowUI = Config.Bind("General", "ShowUI", true, "Show status UI");

            Log.LogInfo("KKS DLSS Upscaler v2.1.1 loaded (native NGX experimental path). Default off, Ctrl+D to enable.");
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (ToggleKey.Value.IsDown() && now >= _nextToggleAllowed)
            {
                // Some KKS input layers can report a shortcut for more than
                // one frame while Ctrl+D is held. Debounce it so one press
                // cannot destroy and recreate the image effect repeatedly.
                _nextToggleAllowed = now + ToggleCooldownSeconds;
                Log.LogInfo($"DLSS toggle pressed; current enabled={EnableDLSS.Value}, component={(_dlssComponent != null ? "present" : "none")}");
                if (EnableDLSS.Value)
                {
                    DisableDLSS();
                }
                else
                {
                    _nativeInitBlocked = false;
                    EnableDLSS.Value = true;
                    // Unity destroys MonoBehaviours at the end of the frame.
                    // Defer re-attachment so the old OnDisable/Dispose and
                    // camera image-effect chain have fully settled first.
                    _toggleTransitionUntil = now + ToggleTransitionSeconds;
                    _nextRetry = _toggleTransitionUntil;
                }
            }
            else if (EnableDLSS.Value && !_nativeInitBlocked && _dlssComponent == null && now >= _nextRetry && now >= _toggleTransitionUntil)
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
                // Studio creates its cameras after the Init scene. This is a
                // normal retry state, not a plugin failure.
                Log.LogInfo("DLSS waiting for Studio camera...");
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
            _toggleTransitionUntil = Time.unscaledTime + ToggleTransitionSeconds;
            _nextRetry = _toggleTransitionUntil;
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
                : _dlssComponent != null && _dlssComponent.IsCapturing
                    ? "DLSS INPUT CAPTURE ONLY | No DLSS output (Ctrl+D to stop)"
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
        private bool _renderEntryLogged;
        private int _outputWarmupFrames;
        private bool _outputReady;
        private bool _outputValidationDone;
        private bool _inputValidationDone;
        private int _guideWarningCooldown;
        private bool _nativeOutputUsable = true;
        private RenderTexture _outputProbeRT;
        private Texture2D _outputProbeTexture;

        // The first few Evaluate calls after feature creation initialize NGX's
        // temporal history. Presenting that relay immediately can expose an
        // all-zero texture during a close -> reopen transition, so keep the
        // source image visible until several completed evaluations are ready.
        private const int OutputWarmupEvaluations = 3;

        public int RenderWidth => _dlss?.RenderWidth ?? 0;
        public int RenderHeight => _dlss?.RenderHeight ?? 0;
        public bool IsActive => _initialized && _dlss != null && _dlss.IsInitialized;
        public bool IsCapturing => _initialized && _dlss != null && _dlss.BridgeActive && !_dlss.IsInitialized;

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
            _outputWarmupFrames = OutputWarmupEvaluations;
            _outputReady = false;
            _outputValidationDone = false;
            _inputValidationDone = false;
            _nativeOutputUsable = true;
            _renderEntryLogged = false;
            _lastFrameTime = Time.realtimeSinceStartup;
            if (_cam != null)
            {
                _cam.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
                int guideW = Mathf.Max(1, Mathf.RoundToInt(Screen.width / PPE_DLSS_Plugin.ScaleFactor.Value));
                int guideH = Mathf.Max(1, Mathf.RoundToInt(Screen.height / PPE_DLSS_Plugin.ScaleFactor.Value));
                _guides = new DLSSGuideCapture(_cam, guideW, guideH);
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
            PPE_DLSS_Plugin.Log?.LogInfo("DLSS component OnDisable; disposing native resources");
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
                if (_dlss.BridgeActive)
                {
                    ScalableBufferManager.ResizeBuffers(_originalScale, _originalScale);
                    _initialized = true;
                    PPE_DLSS_Plugin.Log.LogWarning("Native D3D12 bridge is active in input-capture mode; NGX D3D11 feature is not used.");
                    return;
                }
                PPE_DLSS_Plugin.Log.LogError("DLSS SDK init failed");
                PPE_DLSS_Plugin.Instance?.NotifyInitFailed();
                return;
            }

            _initialized = true;
            _outputWarmupFrames = OutputWarmupEvaluations;
            _outputReady = false;
            _outputValidationDone = false;
            _inputValidationDone = false;
            _nativeOutputUsable = true;
            PPE_DLSS_Plugin.Log.LogInfo("DLSS init successful!");
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!_initialized || _dlss == null)
            {
                if (source != null && destination != null)
                    Graphics.Blit(source, destination);
                return;
            }

            // Unity can invoke image effects during additive scene teardown
            // with one of the temporary render targets already released.
            // Skip that transition frame instead of dereferencing a null RT.
            if (source == null || destination == null || source.width <= 0 || source.height <= 0 || destination.width <= 0 || destination.height <= 0)
            {
                if (source != null && destination != null)
                    Graphics.Blit(source, destination);
                return;
            }

            try
            {
                if (!_renderEntryLogged)
                {
                    _renderEntryLogged = true;
                    PPE_DLSS_Plugin.Log.LogInfo($"DLSS OnRenderImage entered: source={source.width}x{source.height} format={source.format}, destination={destination.width}x{destination.height} format={destination.format}");
                }
                float frameTime = (Time.realtimeSinceStartup - _lastFrameTime) * 1000f;
                _lastFrameTime = Time.realtimeSinceStartup;

                // Copy source to DLSS color input
                var colorRT = _dlss.GetColorTexture();
                if (colorRT != null)
                {
                    Graphics.Blit(source, colorRT);
                    if (!_inputValidationDone)
                    {
                        _inputValidationDone = true;
                        float inputMax = SampleTexture(colorRT);
                        PPE_DLSS_Plugin.Log.LogInfo($"DLSS input color validation: maxChannel={inputMax:F5}");
                    }
                }

                // Unity exposes these camera resources after depthTextureMode is enabled.
                // They are the only native scene guides available without a ReShade bridge.
                Texture sceneDepth = _guides?.DepthTexture ?? Shader.GetGlobalTexture("_CameraDepthTexture");
                Texture sceneMotionVectors = _guides?.MotionTexture ?? Shader.GetGlobalTexture("_CameraMotionVectorsTexture");

                if ((sceneDepth == null || sceneMotionVectors == null) && _guideWarningCooldown-- <= 0)
                {
                    _guideWarningCooldown = 120;
                    PPE_DLSS_Plugin.Log.LogWarning("Native DLSS guide missing: " +
                        (sceneDepth == null ? "depth " : "") +
                        (sceneMotionVectors == null ? "motion-vectors" : ""));
                }

                // Execute DLSS with the live Unity guide resources.
                bool success = _dlss.Evaluate(frameTime, source, sceneDepth, sceneMotionVectors);

                if (success)
                {
                    var outputRT = _dlss.GetOutputTexture();
                    if (outputRT != null)
                    {
                        if (!_outputReady)
                        {
                            if (_outputWarmupFrames > 0)
                            {
                                _outputWarmupFrames--;
                                Graphics.Blit(source, destination);
                                return;
                            }
                            _outputReady = true;
                            PPE_DLSS_Plugin.Log.LogInfo("DLSS output relay warmed up; presenting native output");
                        }
                        if (!_outputValidationDone)
                        {
                            ValidateNativeOutput(outputRT);
                        }
                        if (!_nativeOutputUsable)
                        {
                            // A successful NGX return only proves that the
                            // command completed. If the relay contains no
                            // non-zero pixels, keep the camera visible rather
                            // than presenting an all-black target.
                            Graphics.Blit(source, destination);
                            return;
                        }
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
                PPE_DLSS_Plugin.Log.LogError($"DLSS render exception: {e.Message}\n{e.StackTrace}");
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
            if (_outputProbeTexture != null) UnityEngine.Object.Destroy(_outputProbeTexture);
            if (_outputProbeRT != null) UnityEngine.Object.Destroy(_outputProbeRT);
            _outputProbeTexture = null;
            _outputProbeRT = null;
        }

        private void ValidateNativeOutput(RenderTexture output)
        {
            _outputValidationDone = true;
            try
            {
                if (output == null || !output.IsCreated())
                {
                    _nativeOutputUsable = false;
                    PPE_DLSS_Plugin.Log.LogWarning("DLSS output validation skipped: output texture is not created");
                    return;
                }

                float maxChannel = SampleTexture(output);
                _nativeOutputUsable = maxChannel > 0.0001f;
                PPE_DLSS_Plugin.Log.LogInfo($"DLSS output validation: maxChannel={maxChannel:F5}, usable={_nativeOutputUsable}");
                if (!_nativeOutputUsable)
                    PPE_DLSS_Plugin.Log.LogWarning("DLSS output relay is black; falling back to source presentation");
            }
            catch (Exception e)
            {
                // Validation is diagnostic only. Keep native output enabled if
                // the optional readback is unavailable on this Unity build.
                _nativeOutputUsable = true;
                PPE_DLSS_Plugin.Log.LogWarning($"DLSS output validation unavailable: {e.Message}");
            }
        }

        private float SampleTexture(RenderTexture texture)
        {
            if (texture == null || !texture.IsCreated()) return 0f;
            if (_outputProbeRT == null || !_outputProbeRT.IsCreated())
            {
                if (_outputProbeRT != null) UnityEngine.Object.Destroy(_outputProbeRT);
                if (_outputProbeTexture != null) UnityEngine.Object.Destroy(_outputProbeTexture);
                _outputProbeRT = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32)
                {
                    name = "KKS_DLSS_OutputProbe",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _outputProbeRT.Create();
                _outputProbeTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            }

            Graphics.Blit(texture, _outputProbeRT);
            var previous = RenderTexture.active;
            RenderTexture.active = _outputProbeRT;
            _outputProbeTexture.ReadPixels(new Rect(0, 0, 4, 4), 0, 0, false);
            _outputProbeTexture.Apply(false, false);
            RenderTexture.active = previous;

            float maxChannel = 0f;
            var pixels = _outputProbeTexture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
                maxChannel = Mathf.Max(maxChannel, pixels[i].r, pixels[i].g, pixels[i].b);
            return maxChannel;
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
        private readonly int _width;
        private readonly int _height;
        private CommandBuffer _commandBuffer;
        private RenderTexture _depthTexture;
        private RenderTexture _motionTexture;

        public Texture DepthTexture => _depthTexture;
        public Texture MotionTexture => _motionTexture;

        public DLSSGuideCapture(Camera camera, int width, int height)
        {
            _camera = camera;
            _width = width;
            _height = height;
        }

        public void Install()
        {
            if (_camera == null || _commandBuffer != null) return;

            int width = _width > 0 ? _width : Mathf.Max(1, _camera.pixelWidth);
            int height = _height > 0 ? _height : Mathf.Max(1, _camera.pixelHeight);
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

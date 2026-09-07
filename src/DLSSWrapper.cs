using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PPE_DLSS
{
    internal static class D3D12BridgeNative
    {
        private const string DLL = "kks_dlss_d3d12_bridge.dll";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern uint KKS_DLSS12_Init(string appDataPath);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint KKS_DLSS12_AttachD3D11(IntPtr device, IntPtr context);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint KKS_DLSS12_StageD3D11Texture(uint slot, IntPtr resource);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr KKS_DLSS12_GetD3D12Texture(uint slot);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint KKS_DLSS12_LastResult();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint KKS_DLSS12_LastFeatureResult();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint KKS_DLSS12_LastAttachCode();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void KKS_DLSS12_Shutdown();
    }

    // Native D3D11 helper DLL
    internal static class NativeD3D
    {
        private const string DLL = "d3d11helper.dll";

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr GetDeviceFromTexture(IntPtr texturePtr);

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr GetImmediateContext(IntPtr device);
    }

    public class DLSSWrapper : IDisposable
    {
        private bool _initialized;
        private bool _disposed;
        private IntPtr _parameters;
        private IntPtr _featureHandle;
        private IntPtr _scratchBuffer;
        private uint _scratchSize;

        // Textures
        private RenderTexture _colorRT;
        private RenderTexture _depthRT;
        private RenderTexture _mvRT;
        private RenderTexture _outputRT;

        // D3D11 stuff
        private IntPtr _d3dDevice;
        private IntPtr _d3dContext;
        private bool _bridgeActive;

        public int RenderWidth { get; private set; }
        public int RenderHeight { get; private set; }
        public int OutputWidth { get; private set; }
        public int OutputHeight { get; private set; }

        public bool IsInitialized => _initialized;
        public bool BridgeActive => _bridgeActive;

        private static void NativeLog(string message) => PPE_DLSS_Plugin.Log?.LogInfo("[Native] " + message);
        private static void NativeWarn(string message) => PPE_DLSS_Plugin.Log?.LogWarning("[Native] " + message);
        private static void NativeError(string message) => PPE_DLSS_Plugin.Log?.LogError("[Native] " + message);

        public DLSSWrapper()
        {
        }

        public bool Init(int renderWidth, int renderHeight, int outputWidth, int outputHeight)
        {
            if (_initialized) return true;

            RenderWidth = renderWidth;
            RenderHeight = renderHeight;
            OutputWidth = outputWidth;
            OutputHeight = outputHeight;

            try
            {
                Debug.Log("[DLSS] Starting initialization...");

                // Step 1: Get D3D11 device
                _d3dDevice = GetD3D11Device();
                if (_d3dDevice == IntPtr.Zero)
                {
                    NativeError("GetD3D11Device returned null");
                    Debug.LogError("[DLSS] Failed to get D3D11 device");
                    return false;
                }
                Debug.Log($"[DLSS] Got D3D11 device: 0x{_d3dDevice.ToInt64():X}");

                // Get immediate context
                _d3dContext = GetImmediateContext(_d3dDevice);
                NativeLog($"D3D11 device=0x{_d3dDevice.ToInt64():X}, context=0x{_d3dContext.ToInt64():X}");
                Debug.Log($"[DLSS] Got D3D11 context: 0x{_d3dContext.ToInt64():X}");

                TryStartD3D12Bridge();

                // Step 2: Verify DLL works
                try
                {
                    uint apiVer = DLSSNative.GetAPIVersion();
                    uint driverVer = DLSSNative.GetDriverVersion();
                    Debug.Log($"[DLSS] API version: 0x{apiVer:X}, Driver version: 0x{driverVer:X} ({driverVer})");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DLSS] GetVersion failed: {e.Message}");
                }

                // Step 3: Init NGX - try Unity device first, then standalone D3D11 device
                ulong[] appIds = { 0, 0x4B4B5353554E5348, 0x444C535354455354 };
                string[] dataPaths = { null, "", Application.persistentDataPath + "\\NGX" };
                uint[] sdkVersions = { 0x00000013, 0x00000015, 0x00010000, 0 };
                
                IntPtr[] devices = { _d3dDevice, IntPtr.Zero };
                IntPtr standaloneDevice = IntPtr.Zero;
                IntPtr standaloneContext = IntPtr.Zero;
                
                // Try to create standalone device
                try
                {
                    uint featureLevel;
                    int hr = DLSSNative.D3D11CreateDevice(
                        IntPtr.Zero, 1, IntPtr.Zero, 0, IntPtr.Zero, 0, 7,
                        out standaloneDevice, out featureLevel, out standaloneContext);
                    Debug.Log($"[DLSS] Standalone device: hr=0x{hr:X}, device=0x{standaloneDevice.ToInt64():X}, ctx=0x{standaloneContext.ToInt64():X}, fl=0x{featureLevel:X}");
                }
                catch (Exception e)
                {
                    Debug.Log($"[DLSS] Standalone device create failed: {e.Message}");
                }
                
                if (standaloneDevice != IntPtr.Zero)
                    devices[1] = standaloneDevice;
                
                DLSSNative.NVSDK_NGX_Result initResult = DLSSNative.NVSDK_NGX_Result.Fail;
                bool initOk = false;
                
                foreach (var dev in devices)
                {
                    if (dev == IntPtr.Zero) continue;
                    string devName = (dev == _d3dDevice) ? "Unity" : "Standalone";
                    Debug.Log($"[DLSS] Trying device: {devName} (0x{dev.ToInt64():X})");

                    // KKS has no NVIDIA-issued application ID. The public NGX API
                    // supports a GUID-like project identifier for custom engines.
                    foreach (var dp in dataPaths)
                    {
                        try
                        {
                            initResult = DLSSNative.D3D11_Init_with_ProjectID(
                                "6a9c4c0d-6f1c-4c4d-9a70-6f2d0f4f2c19",
                                0, // NVSDK_NGX_ENGINE_TYPE_CUSTOM
                                "Unity 2019.4.9f1 KKS CharaStudio",
                                dp,
                                dev,
                                IntPtr.Zero,
                                0x00000015);
                            if (initResult == DLSSNative.NVSDK_NGX_Result.Success || initResult == DLSSNative.NVSDK_NGX_Result.AlreadyInitialized)
                            {
                                NativeLog($"ProjectID NGX init succeeded on {devName}, path={(dp ?? "null")}");
                                initOk = true;
                                _d3dDevice = dev;
                                if (dev == standaloneDevice) _d3dContext = standaloneContext;
                                break;
                            }
                        }
                        catch (Exception e)
                        {
                            NativeWarn($"ProjectID init exception on {devName}: {e.Message}");
                        }
                    }
                    if (initOk) break;
                    
                    foreach (var aid in appIds)
                    {
                        foreach (var dp in dataPaths)
                        {
                            foreach (var sv in sdkVersions)
                            {
                                try
                                {
                                    initResult = DLSSNative.D3D11_Init(aid, dp, dev, IntPtr.Zero, sv);
                                    if (initResult == DLSSNative.NVSDK_NGX_Result.Success || initResult == DLSSNative.NVSDK_NGX_Result.AlreadyInitialized)
                                    {
                                        Debug.Log($"[DLSS] SUCCESS! appId=0x{aid:X}, dataPath={(dp == null ? "null" : dp)}, sdk=0x{sv:X}, device={devName}");
                                        initOk = true;
                                        _d3dDevice = dev;
                                        if (dev == standaloneDevice)
                                            _d3dContext = standaloneContext;
                                        break;
                                    }
                                }
                                catch (Exception e)
                                {
                                    Debug.Log($"[DLSS] Init exception: {e.Message}");
                                }
                            }
                            if (initOk) break;
                        }
                        if (initOk) break;
                    }
                    if (initOk) break;
                }
                
                if (!initOk)
                {
                    NativeError($"All NGX D3D11 init attempts failed; last result={initResult}");
                    Debug.LogError($"[DLSS] All init attempts failed, last result: {initResult}");
                    return false;
                }
                Debug.Log($"[DLSS] NGX Init success!");

                // Step 3: Get parameters
                var getParamsResult = DLSSNative.D3D11_GetParameters(out _parameters);
                Debug.Log($"[DLSS] GetParameters result: {getParamsResult}, ptr=0x{_parameters.ToInt64():X}");
                if (getParamsResult != DLSSNative.NVSDK_NGX_Result.Success || _parameters == IntPtr.Zero)
                {
                    NativeError($"D3D11_GetParameters failed: {getParamsResult}, ptr=0x{_parameters.ToInt64():X}");
                    Debug.LogError("[DLSS] GetParameters failed");
                    return false;
                }

                using (var param = new NGXParameter(_parameters, ownsParameters: false))
                {
                    // Step 4: Set basic dimensions
                    param.Set("Width", (uint)renderWidth);
                    param.Set("Height", (uint)renderHeight);
                    param.Set("OutWidth", (uint)outputWidth);
                    param.Set("OutHeight", (uint)outputHeight);
                    Debug.Log("[DLSS] Set dimensions");

                    // Step 5: Get scratch buffer size
                    var scratchResult = DLSSNative.D3D11_GetScratchBufferSize(
                        DLSSNative.NVSDK_NGX_Feature.SuperResolution,
                        _parameters, out _scratchSize);
                    Debug.Log($"[DLSS] GetScratchBufferSize result: {scratchResult}, size={_scratchSize}");
                    if (scratchResult != DLSSNative.NVSDK_NGX_Result.Success || _scratchSize == 0)
                    {
                        // Try with default size
                        _scratchSize = 64 * 1024 * 1024; // 64MB default
                        Debug.LogWarning($"[DLSS] Using default scratch size: {_scratchSize}");
                    }

                    // Step 6: Allocate scratch buffer
                    _scratchBuffer = Marshal.AllocHGlobal((int)_scratchSize);
                    Debug.Log($"[DLSS] Allocated scratch buffer: 0x{_scratchBuffer.ToInt64():X}, size={_scratchSize}");

                    param.Set("Scratch", _scratchBuffer);
                    param.Set("Scratch.SizeInBytes", _scratchSize);

                    // Step 7: Create textures
                    CreateTextures();

                    // Set texture resources
                    param.SetD3D11Resource("Color", GetNativeTexturePtr(_colorRT));
                    param.SetD3D11Resource("Depth", GetNativeTexturePtr(_depthRT));
                    param.SetD3D11Resource("MotionVectors", GetNativeTexturePtr(_mvRT));
                    param.SetD3D11Resource("Output", GetNativeTexturePtr(_outputRT));
                    Debug.Log("[DLSS] Set texture resources");

                    // Step 8: Set DLSS creation params
                    param.Set("PerfQualityValue", 2); // 0=maxPerf,1=balanced,2=quality,3=maxQuality
                    param.Set("DLSS.Feature.Create.Flags", 1); // HDR; guide flags are added as inputs are validated
                    param.Set("MV.Scale.X", 1.0f);
                    param.Set("MV.Scale.Y", 1.0f);
                    param.Set("Reset", (uint)1);
                    Debug.Log("[DLSS] Set DLSS params");
                }

                // Step 9: Create feature
                var createResult = DLSSNative.D3D11_CreateFeature(
                    _d3dContext,
                    DLSSNative.NVSDK_NGX_Feature.SuperResolution,
                    _parameters,
                    out _featureHandle);
                Debug.Log($"[DLSS] CreateFeature result: {createResult}, handle=0x{_featureHandle.ToInt64():X}");
                if (createResult != DLSSNative.NVSDK_NGX_Result.Success || _featureHandle == IntPtr.Zero)
                {
                    NativeError($"D3D11_CreateFeature failed: {createResult}, handle=0x{_featureHandle.ToInt64():X}");
                    Debug.LogError($"[DLSS] CreateFeature failed: {createResult}");
                    return false;
                }

                _initialized = true;
                Debug.Log("[DLSS] Initialization successful!");
                return true;
            }
            catch (Exception e)
            {
                NativeError($"Init exception: {e.Message}");
                Debug.LogError($"[DLSS] Init exception: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        public bool Evaluate(float frameTimeMs, Texture sceneDepth, Texture sceneMotionVectors)
        {
            if (!_initialized)
            {
                StageBridgeInputs(sceneDepth, sceneMotionVectors);
                return false;
            }

            try
            {
                using (var param = new NGXParameter(_parameters, ownsParameters: false))
                {
                    // Bind Unity's live camera resources at evaluate time. The previous
                    // proof-of-concept passed an empty depth RT and a zeroed MV RT.
                    IntPtr depthPtr = sceneDepth != null ? sceneDepth.GetNativeTexturePtr() : IntPtr.Zero;
                    IntPtr motionPtr = sceneMotionVectors != null ? sceneMotionVectors.GetNativeTexturePtr() : IntPtr.Zero;
                    StageBridgeInputs(sceneDepth, sceneMotionVectors);
                    if (depthPtr != IntPtr.Zero)
                        param.SetD3D11Resource("Depth", depthPtr);
                    if (motionPtr != IntPtr.Zero)
                        param.SetD3D11Resource("MotionVectors", motionPtr);

                    param.SetD3D11Resource("Color", _colorRT.GetNativeTexturePtr());
                    param.SetD3D11Resource("Output", _outputRT.GetNativeTexturePtr());

                    param.Set("FrameTimeDeltaInMsec", frameTimeMs);
                    param.Set("Reset", (uint)0);
                    param.Set("Jitter.Offset.X", 0.0f);
                    param.Set("Jitter.Offset.Y", 0.0f);
                }

                var result = DLSSNative.D3D11_EvaluateFeature_C(
                    _d3dContext, _featureHandle, _parameters, IntPtr.Zero);

                if (result != DLSSNative.NVSDK_NGX_Result.Success)
                {
                    Debug.LogWarning($"[DLSS] Evaluate result: {result}");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DLSS] Evaluate exception: {e.Message}");
                return false;
            }
        }

        public RenderTexture GetOutputTexture()
        {
            return _outputRT;
        }

        public RenderTexture GetColorTexture()
        {
            return _colorRT;
        }

        private void CreateTextures()
        {
            // Color input - HDR format
            _colorRT = new RenderTexture(RenderWidth, RenderHeight, 0, RenderTextureFormat.ARGBHalf);
            _colorRT.enableRandomWrite = true;
            _colorRT.Create();
            Debug.Log($"[DLSS] Created color RT: {RenderWidth}x{RenderHeight}");

            // Depth input
            _depthRT = new RenderTexture(RenderWidth, RenderHeight, 24, RenderTextureFormat.Depth);
            _depthRT.Create();
            Debug.Log($"[DLSS] Created depth RT: {RenderWidth}x{RenderHeight}");

            // Motion vectors - zero for now
            _mvRT = new RenderTexture(RenderWidth, RenderHeight, 0, RenderTextureFormat.RGHalf);
            _mvRT.enableRandomWrite = true;
            _mvRT.Create();
            // Clear to zero
            RenderTexture.active = _mvRT;
            GL.Clear(false, true, new Color(0, 0, 0, 0));
            RenderTexture.active = null;
            Debug.Log($"[DLSS] Created MV RT: {RenderWidth}x{RenderHeight} (zeroed)");

            // Output
            _outputRT = new RenderTexture(OutputWidth, OutputHeight, 0, RenderTextureFormat.ARGBHalf);
            _outputRT.enableRandomWrite = true;
            _outputRT.Create();
            Debug.Log($"[DLSS] Created output RT: {OutputWidth}x{OutputHeight}");
        }

        private IntPtr GetNativeTexturePtr(RenderTexture rt)
        {
            if (rt == null) return IntPtr.Zero;
            return rt.GetNativeTexturePtr();
        }

        private IntPtr GetD3D11Device()
        {
            try
            {
                var tempRT = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
                tempRT.Create();
                IntPtr texPtr = tempRT.GetNativeTexturePtr();
                Debug.Log($"[DLSS] Texture ptr: 0x{texPtr.ToInt64():X}");

                if (texPtr == IntPtr.Zero)
                {
                    Debug.LogError("[DLSS] Texture ptr is zero");
                    UnityEngine.Object.Destroy(tempRT);
                    return IntPtr.Zero;
                }

                // Use native helper DLL
                IntPtr device = NativeD3D.GetDeviceFromTexture(texPtr);
                Debug.Log($"[DLSS] Device from native DLL: 0x{device.ToInt64():X}");

                UnityEngine.Object.Destroy(tempRT);

                if (device == IntPtr.Zero || device.ToInt64() < 0x10000)
                {
                    Debug.LogError($"[DLSS] Invalid device pointer: 0x{device.ToInt64():X}");
                    return IntPtr.Zero;
                }
                return device;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DLSS] GetD3D11Device exception: {e.Message}\n{e.StackTrace}");
                return IntPtr.Zero;
            }
        }

        private void TryStartD3D12Bridge()
        {
            try
            {
                string root = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(root)) return;
                uint init = D3D12BridgeNative.KKS_DLSS12_Init(root);
                uint attach = init == 1 ? D3D12BridgeNative.KKS_DLSS12_AttachD3D11(_d3dDevice, _d3dContext) : 0;
                _bridgeActive = init == 1 && attach == 1;
                NativeLog($"D3D12 bridge init=0x{init:X8}, feature=0x{D3D12BridgeNative.KKS_DLSS12_LastFeatureResult():X8}, attach={attach}, attachCode={D3D12BridgeNative.KKS_DLSS12_LastAttachCode()}, active={_bridgeActive}");
            }
            catch (Exception e)
            {
                _bridgeActive = false;
                NativeWarn($"D3D12 bridge unavailable: {e.Message}");
            }
        }

        private void StageBridgeInputs(Texture sceneDepth, Texture sceneMotionVectors)
        {
            if (!_bridgeActive) return;
            try
            {
                IntPtr color = _colorRT != null ? _colorRT.GetNativeTexturePtr() : IntPtr.Zero;
                IntPtr depth = sceneDepth != null ? sceneDepth.GetNativeTexturePtr() : IntPtr.Zero;
                IntPtr motion = sceneMotionVectors != null ? sceneMotionVectors.GetNativeTexturePtr() : IntPtr.Zero;
                if (color != IntPtr.Zero) D3D12BridgeNative.KKS_DLSS12_StageD3D11Texture(0, color);
                if (depth != IntPtr.Zero) D3D12BridgeNative.KKS_DLSS12_StageD3D11Texture(1, depth);
                if (motion != IntPtr.Zero) D3D12BridgeNative.KKS_DLSS12_StageD3D11Texture(2, motion);
            }
            catch (Exception e)
            {
                NativeWarn($"D3D12 bridge staging failed: {e.Message}");
            }
        }

        private IntPtr GetImmediateContext(IntPtr device)
        {
            try
            {
                IntPtr ctx = NativeD3D.GetImmediateContext(device);
                Debug.Log($"[DLSS] Context from native DLL: 0x{ctx.ToInt64():X}");
                return ctx;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DLSS] GetImmediateContext exception: {e.Message}");
                return device;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_featureHandle != IntPtr.Zero)
                {
                    DLSSNative.D3D11_ReleaseFeature(_featureHandle);
                    _featureHandle = IntPtr.Zero;
                }
                if (_scratchBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_scratchBuffer);
                    _scratchBuffer = IntPtr.Zero;
                }
                if (_colorRT != null) { UnityEngine.Object.Destroy(_colorRT); _colorRT = null; }
                if (_depthRT != null) { UnityEngine.Object.Destroy(_depthRT); _depthRT = null; }
                if (_mvRT != null) { UnityEngine.Object.Destroy(_mvRT); _mvRT = null; }
                if (_outputRT != null) { UnityEngine.Object.Destroy(_outputRT); _outputRT = null; }
                if (_initialized)
                {
                    DLSSNative.D3D11_Shutdown();
                }
                if (_bridgeActive)
                {
                    D3D12BridgeNative.KKS_DLSS12_Shutdown();
                    _bridgeActive = false;
                }
                _initialized = false;
                Debug.Log("[DLSS] Disposed");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DLSS] Dispose exception: {e.Message}");
            }
        }
    }
}

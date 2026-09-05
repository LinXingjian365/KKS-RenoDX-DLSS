using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PPE_DLSS
{
    // DLSS NGX Core API (nvngx.dll) - D3D11
    public static class DLSSNative
    {
        private const string NGX_DLL = "nvngx_dlss.dll";

        public enum NVSDK_NGX_Result : uint
        {
            Success = 0x1,
            Fail = 0xBAD00000,
            FeatureNotSupported = 0xBAD00001,
            PlatformError = 0xBAD00002,
            FeatureAlreadyExists = 0xBAD00003,
            FeatureNotFound = 0xBAD00004,
            InvalidParameter = 0xBAD00005,
            ScratchBufferTooSmall = 0xBAD00006,
            NotInitialized = 0xBAD00007,
            UnsupportedInputFormat = 0xBAD00008,
            RWFlagMissing = 0xBAD00009,
            MissingInput = 0xBAD0000A,
            UnableToInitializeFeature = 0xBAD0000B,
            OutOfDate = 0xBAD0000C,
            OutOfGPUMemory = 0xBAD0000D,
            UnsupportedFormat = 0xBAD0000E,
            UnableToWriteToAppDataPath = 0xBAD0000F,
            UnsupportedParameter = 0xBAD00010,
            Denied = 0xBAD00011,
            NotImplemented = 0xBAD00012,
            AlreadyInitialized = 0xBAD00013,
            UnknownError = 0xFFFFFFFF
        }

        public enum NVSDK_NGX_Feature : uint
        {
            SuperResolution = 0,
            RayReconstruction = 1,
            ImageSuperResolution = 2,
            Depth = 3
        }

        // NGX Core D3D11 API - snippet build (5 params: appId, dataPath, device, FeatureCommonInfo*, sdkVersion)
        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_D3D11_Init", CallingConvention = CallingConvention.Cdecl)]
        public static extern NVSDK_NGX_Result D3D11_Init(
            ulong InApplicationId,
            [MarshalAs(UnmanagedType.LPWStr)] string InApplicationDataPath,
            IntPtr InDevice,
            IntPtr InFeatureInfo,
            uint InSDKVersion);

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_D3D11_Init_Ext", CallingConvention = CallingConvention.Cdecl)]
        public static extern NVSDK_NGX_Result D3D11_Init_Ext(
            ulong InApplicationId,
            [MarshalAs(UnmanagedType.LPWStr)] string InApplicationDataPath,
            IntPtr InDevice,
            uint InSDKVersion,
            IntPtr InParameters);

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_D3D11_Shutdown", CallingConvention = CallingConvention.Cdecl)]
        public static extern NVSDK_NGX_Result D3D11_Shutdown();

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_D3D11_GetParameters", CallingConvention = CallingConvention.Cdecl)]
        public static extern NVSDK_NGX_Result D3D11_GetParameters(out IntPtr OutParameters);

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_D3D11_AllocateParameters", CallingConvention = CallingConvention.Cdecl)]
        public static extern NVSDK_NGX_Result D3D11_AllocateParameters(out IntPtr OutParameters);

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_D3D11_DestroyParameters", CallingConvention = CallingConvention.Cdecl)]
        public static extern NVSDK_NGX_Result D3D11_DestroyParameters(IntPtr InParameters);

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_D3D11_CreateFeature", CallingConvention = CallingConvention.Cdecl)]
        public static extern NVSDK_NGX_Result D3D11_CreateFeature(
            IntPtr InDevCtx,
            NVSDK_NGX_Feature InFeatureID,
            IntPtr InParameters,
            out IntPtr OutHandle);

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_D3D11_ReleaseFeature", CallingConvention = CallingConvention.Cdecl)]
        public static extern NVSDK_NGX_Result D3D11_ReleaseFeature(IntPtr InHandle);

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_D3D11_EvaluateFeature", CallingConvention = CallingConvention.Cdecl)]
        public static extern NVSDK_NGX_Result D3D11_EvaluateFeature(
            IntPtr InDevCtx,
            IntPtr InFeatureHandle,
            IntPtr InParameters,
            IntPtr InCallback);

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_D3D11_GetScratchBufferSize", CallingConvention = CallingConvention.Cdecl)]
        public static extern NVSDK_NGX_Result D3D11_GetScratchBufferSize(
            NVSDK_NGX_Feature InFeature,
            IntPtr InParameters,
            out uint OutSize);

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_GetAPIVersion", CallingConvention = CallingConvention.Cdecl)]
        public static extern uint GetAPIVersion();

        [DllImport(NGX_DLL, EntryPoint = "NVSDK_NGX_GetDriverVersion", CallingConvention = CallingConvention.Cdecl)]
        public static extern uint GetDriverVersion();

        // D3D11CreateDevice for creating a standalone device
        [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", CallingConvention = CallingConvention.StdCall)]
        public static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            uint DriverType,
            IntPtr Software,
            uint Flags,
            IntPtr pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,
            out IntPtr ppDevice,
            out uint pFeatureLevel,
            out IntPtr ppImmediateContext);
    }

    // NVSDK_NGX_Parameter wrapper - calls Set via vtable
    public class NGXParameter : IDisposable
    {
        public IntPtr Ptr { get; private set; }
        private IntPtr _vtable;
        private bool _disposed;

        // vtable delegates
        private delegate void SetULLDelegate(IntPtr self, IntPtr name, ulong value);
        private delegate void SetFDelegate(IntPtr self, IntPtr name, float value);
        private delegate void SetUIDelegate(IntPtr self, IntPtr name, uint value);
        private delegate void SetIDelegate(IntPtr self, IntPtr name, int value);
        private delegate void SetPtrDelegate(IntPtr self, IntPtr name, IntPtr value);
        private delegate void ResetDelegate(IntPtr self);

        private SetULLDelegate _setULL;
        private SetFDelegate _setF;
        private SetUIDelegate _setUI;
        private SetIDelegate _setI;
        private SetPtrDelegate _setPtr;
        private ResetDelegate _reset;

        public NGXParameter(IntPtr ptr)
        {
            Ptr = ptr;
            if (ptr != IntPtr.Zero)
            {
                _vtable = Marshal.ReadIntPtr(ptr, 0);
                InitDelegates();
            }
        }

        private void InitDelegates()
        {
            int ptrSize = IntPtr.Size;
            // vtable indices: 0=SetULL, 1=SetF, 2=SetD, 3=SetUI, 4=SetI, 5=SetD3D11, 6=SetD3D12, 7=SetVoid, 16=Reset
            _setULL = Marshal.GetDelegateForFunctionPointer<SetULLDelegate>(Marshal.ReadIntPtr(_vtable, 0 * ptrSize));
            _setF = Marshal.GetDelegateForFunctionPointer<SetFDelegate>(Marshal.ReadIntPtr(_vtable, 1 * ptrSize));
            _setUI = Marshal.GetDelegateForFunctionPointer<SetUIDelegate>(Marshal.ReadIntPtr(_vtable, 3 * ptrSize));
            _setI = Marshal.GetDelegateForFunctionPointer<SetIDelegate>(Marshal.ReadIntPtr(_vtable, 4 * ptrSize));
            _setPtr = Marshal.GetDelegateForFunctionPointer<SetPtrDelegate>(Marshal.ReadIntPtr(_vtable, 7 * ptrSize));
            _reset = Marshal.GetDelegateForFunctionPointer<ResetDelegate>(Marshal.ReadIntPtr(_vtable, 16 * ptrSize));
        }

        public void Set(string name, ulong value)
        {
            IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
            try { _setULL(Ptr, namePtr, value); }
            finally { Marshal.FreeHGlobal(namePtr); }
        }

        public void Set(string name, float value)
        {
            IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
            try { _setF(Ptr, namePtr, value); }
            finally { Marshal.FreeHGlobal(namePtr); }
        }

        public void Set(string name, uint value)
        {
            IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
            try { _setUI(Ptr, namePtr, value); }
            finally { Marshal.FreeHGlobal(namePtr); }
        }

        public void Set(string name, int value)
        {
            IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
            try { _setI(Ptr, namePtr, value); }
            finally { Marshal.FreeHGlobal(namePtr); }
        }

        public void Set(string name, IntPtr value)
        {
            IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
            try { _setPtr(Ptr, namePtr, value); }
            finally { Marshal.FreeHGlobal(namePtr); }
        }

        public void Reset()
        {
            if (_reset != null) _reset(Ptr);
        }

        public void Dispose()
        {
            if (!_disposed && Ptr != IntPtr.Zero)
            {
                DLSSNative.D3D11_DestroyParameters(Ptr);
                Ptr = IntPtr.Zero;
            }
            _disposed = true;
        }
    }
}

#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <mutex>
#include <string>
#include "third_party/DLSS/include/nvsdk_ngx.h"

namespace
{
    HMODULE g_ngx = nullptr;
    ID3D12Device* g_device = nullptr;
    NVSDK_NGX_Result g_last = NVSDK_NGX_Result_FAIL_NotInitialized;
    unsigned long long g_appId = 0;
    std::mutex g_mutex;

    template <typename T>
    T resolve(const char* name)
    {
        return g_ngx ? reinterpret_cast<T>(GetProcAddress(g_ngx, name)) : nullptr;
    }

    void releaseDevice()
    {
        if (g_device)
        {
            using ShutdownFn = NVSDK_NGX_Result (NVSDK_CONV *)(ID3D12Device*);
            auto shutdown = resolve<ShutdownFn>("NVSDK_NGX_D3D12_Shutdown1");
            if (shutdown)
            {
                __try { shutdown(g_device); }
                __except (EXCEPTION_EXECUTE_HANDLER) { }
            }
            g_device->Release();
            g_device = nullptr;
        }
        if (g_ngx)
        {
            FreeLibrary(g_ngx);
            g_ngx = nullptr;
        }
    }

    template <typename F>
    NVSDK_NGX_Result guarded(F&& call)
    {
        __try
        {
            return call();
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return NVSDK_NGX_Result_FAIL_PlatformError;
        }
    }
}

extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_Init(const wchar_t* appDataPath)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    releaseDevice();

    const wchar_t* path = appDataPath ? appDataPath : L".";
    std::wstring ngxPath(path);
    if (!ngxPath.empty() && ngxPath.back() != L'\\') ngxPath += L'\\';
    ngxPath += L"_nvngx.dll";
    g_ngx = LoadLibraryW(ngxPath.c_str());
    if (!g_ngx) g_ngx = LoadLibraryW(L"_nvngx.dll");
    if (!g_ngx)
    {
        g_last = NVSDK_NGX_Result_FAIL_PlatformError;
        return static_cast<unsigned int>(g_last);
    }

    IDXGIFactory6* factory = nullptr;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(hr))
    {
        g_last = NVSDK_NGX_Result_FAIL_PlatformError;
        releaseDevice();
        return static_cast<unsigned int>(g_last);
    }

    IDXGIAdapter1* adapter = nullptr;
    for (UINT i = 0; factory->EnumAdapterByGpuPreference(i, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, IID_PPV_ARGS(&adapter)) != DXGI_ERROR_NOT_FOUND; ++i)
    {
        DXGI_ADAPTER_DESC1 desc{};
        adapter->GetDesc1(&desc);
        if (!(desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE)) break;
        adapter->Release();
        adapter = nullptr;
    }

    hr = D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&g_device));
    if (adapter) adapter->Release();
    factory->Release();
    if (FAILED(hr) || !g_device)
    {
        g_last = NVSDK_NGX_Result_FAIL_PlatformError;
        releaseDevice();
        return static_cast<unsigned int>(g_last);
    }

    using InitFn = NVSDK_NGX_Result (NVSDK_CONV *)(const char*, NVSDK_NGX_EngineType, const char*, const wchar_t*, ID3D12Device*, const NVSDK_NGX_FeatureCommonInfo*, NVSDK_NGX_Version);
    auto initProject = resolve<InitFn>("NVSDK_NGX_D3D12_Init_ProjectID");
    using StandardInitFn = NVSDK_NGX_Result (NVSDK_CONV *)(unsigned long long, const wchar_t*, ID3D12Device*, const NVSDK_NGX_FeatureCommonInfo*, NVSDK_NGX_Version);
    auto initStandard = resolve<StandardInitFn>("NVSDK_NGX_D3D12_Init");
    if (!initProject && !initStandard)
    {
        g_last = NVSDK_NGX_Result_FAIL_PlatformError;
        releaseDevice();
        return static_cast<unsigned int>(g_last);
    }

    NVSDK_NGX_FeatureCommonInfo common{};
    if (initProject)
    {
        g_last = guarded([&]() { return initProject(
            "6a9c4c0d-6f1c-4c4d-9a70-6f2d0f4f2c19",
            NVSDK_NGX_ENGINE_TYPE_CUSTOM,
            "Unity 2019.4.9f1 KKS CharaStudio",
            path,
            g_device,
            &common,
            NVSDK_NGX_Version_API); });
    }

    // Feeder proves that some private D3D12 sessions use the standard entry
    // point. Try only documented/diagnostic IDs; never invent a game identity.
    if (g_last != NVSDK_NGX_Result_Success && initStandard)
    {
        const unsigned long long appIds[] = { 0ull, 0x4B4B5353554E5348ull, 0x444C535354455354ull };
        for (unsigned long long appId : appIds)
        {
            g_last = guarded([&]() { return initStandard(appId, path, g_device, &common, NVSDK_NGX_Version_API); });
            if (g_last == NVSDK_NGX_Result_Success)
            {
                g_appId = appId;
                break;
            }
        }
    }

    if (g_last != NVSDK_NGX_Result_Success)
        releaseDevice();
    return static_cast<unsigned int>(g_last);
}

extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_LastResult()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return static_cast<unsigned int>(g_last);
}

extern "C" __declspec(dllexport) unsigned long long __cdecl KKS_DLSS12_LastAppId()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_appId;
}

extern "C" __declspec(dllexport) void __cdecl KKS_DLSS12_Shutdown()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    releaseDevice();
    g_last = NVSDK_NGX_Result_FAIL_NotInitialized;
    g_appId = 0;
}

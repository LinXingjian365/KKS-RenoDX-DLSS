#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <mutex>
#include <string>
#include "third_party/DLSS/include/nvsdk_ngx.h"
#include "third_party/DLSS/include/nvsdk_ngx_params.h"

namespace
{
    HMODULE g_ngx = nullptr;
    ID3D12Device* g_device = nullptr;
    NVSDK_NGX_Result g_last = NVSDK_NGX_Result_FAIL_NotInitialized;
    NVSDK_NGX_Result g_feature = NVSDK_NGX_Result_FAIL_NotInitialized;
    unsigned long long g_appId = 0;
    ID3D12CommandQueue* g_queue = nullptr;
    ID3D12CommandAllocator* g_allocator = nullptr;
    ID3D12GraphicsCommandList* g_list = nullptr;
    NVSDK_NGX_Parameter* g_params = nullptr;
    NVSDK_NGX_Handle* g_handle = nullptr;
    ID3D12Resource* g_color = nullptr;
    ID3D12Resource* g_depth = nullptr;
    ID3D12Resource* g_motion = nullptr;
    ID3D12Resource* g_output = nullptr;
    std::mutex g_mutex;

    template <typename T>
    T resolve(const char* name)
    {
        return g_ngx ? reinterpret_cast<T>(GetProcAddress(g_ngx, name)) : nullptr;
    }

    void releaseDevice()
    {
        if (g_handle)
        {
            using ReleaseFn = NVSDK_NGX_Result (NVSDK_CONV *)(NVSDK_NGX_Handle*);
            auto release = resolve<ReleaseFn>("NVSDK_NGX_D3D12_ReleaseFeature");
            if (release) { __try { release(g_handle); } __except (EXCEPTION_EXECUTE_HANDLER) { } }
            g_handle = nullptr;
        }
        if (g_params)
        {
            using DestroyFn = NVSDK_NGX_Result (NVSDK_CONV *)(NVSDK_NGX_Parameter*);
            auto destroy = resolve<DestroyFn>("NVSDK_NGX_D3D12_DestroyParameters");
            if (destroy) { __try { destroy(g_params); } __except (EXCEPTION_EXECUTE_HANDLER) { } }
            g_params = nullptr;
        }
        if (g_output) { g_output->Release(); g_output = nullptr; }
        if (g_motion) { g_motion->Release(); g_motion = nullptr; }
        if (g_depth) { g_depth->Release(); g_depth = nullptr; }
        if (g_color) { g_color->Release(); g_color = nullptr; }
        if (g_list) { g_list->Release(); g_list = nullptr; }
        if (g_allocator) { g_allocator->Release(); g_allocator = nullptr; }
        if (g_queue) { g_queue->Release(); g_queue = nullptr; }
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

    bool createTestFeature()
    {
        using AllocateFn = NVSDK_NGX_Result (NVSDK_CONV *)(NVSDK_NGX_Parameter**);
        using CreateFn = NVSDK_NGX_Result (NVSDK_CONV *)(ID3D12GraphicsCommandList*, NVSDK_NGX_Feature, NVSDK_NGX_Parameter*, NVSDK_NGX_Handle**);
        auto allocate = resolve<AllocateFn>("NVSDK_NGX_D3D12_AllocateParameters");
        auto create = resolve<CreateFn>("NVSDK_NGX_D3D12_CreateFeature");
        if (!allocate || !create || !g_device) { g_feature = NVSDK_NGX_Result_FAIL_PlatformError; return false; }

        D3D12_COMMAND_QUEUE_DESC queueDesc{};
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        if (FAILED(g_device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&g_queue)))) { g_feature = NVSDK_NGX_Result_FAIL_PlatformError; return false; }
        if (FAILED(g_device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&g_allocator)))) { g_feature = NVSDK_NGX_Result_FAIL_PlatformError; return false; }
        if (FAILED(g_device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, g_allocator, nullptr, IID_PPV_ARGS(&g_list)))) { g_feature = NVSDK_NGX_Result_FAIL_PlatformError; return false; }

        g_feature = guarded([&]() { return allocate(&g_params); });
        if (g_feature != NVSDK_NGX_Result_Success || !g_params) return false;

        const UINT width = 1280;
        const UINT height = 720;
        auto makeTexture = [&](DXGI_FORMAT format, D3D12_RESOURCE_FLAGS flags, D3D12_RESOURCE_STATES state, ID3D12Resource** result)
        {
            D3D12_HEAP_PROPERTIES heap{};
            heap.Type = D3D12_HEAP_TYPE_DEFAULT;
            D3D12_RESOURCE_DESC desc{};
            desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
            desc.Width = width;
            desc.Height = height;
            desc.DepthOrArraySize = 1;
            desc.MipLevels = 1;
            desc.Format = format;
            desc.SampleDesc.Count = 1;
            desc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
            desc.Flags = flags;
            return g_device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc, state, nullptr, IID_PPV_ARGS(result));
        };
        if (FAILED(makeTexture(DXGI_FORMAT_R8G8B8A8_UNORM, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, &g_color)) ||
            FAILED(makeTexture(DXGI_FORMAT_R32_FLOAT, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, &g_depth)) ||
            FAILED(makeTexture(DXGI_FORMAT_R16G16_FLOAT, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, &g_motion)) ||
            FAILED(makeTexture(DXGI_FORMAT_R8G8B8A8_UNORM, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, &g_output)))
        {
            g_feature = NVSDK_NGX_Result_FAIL_PlatformError;
            return false;
        }

        g_params->Set("Width", width);
        g_params->Set("Height", height);
        g_params->Set("OutWidth", width);
        g_params->Set("OutHeight", height);
        g_params->Set("CreationNodeMask", (unsigned int)1);
        g_params->Set("VisibilityNodeMask", (unsigned int)1);
        g_params->Set("PerfQualityValue", (int)NVSDK_NGX_PerfQuality_Value_DLAA);
        g_params->Set("DLSS.Feature.Create.Flags", (int)(NVSDK_NGX_DLSS_Feature_Flags_MVLowRes | NVSDK_NGX_DLSS_Feature_Flags_DepthInverted | NVSDK_NGX_DLSS_Feature_Flags_AutoExposure));
        g_params->Set("DLSS.Enable.Output.Subrects", (int)0);
        g_params->Set("Reset", (int)1);
        g_params->Set("Jitter.Offset.X", 0.0f);
        g_params->Set("Jitter.Offset.Y", 0.0f);
        g_params->Set("MV.Scale.X", 1.0f);
        g_params->Set("MV.Scale.Y", 1.0f);
        g_params->Set("Color", g_color);
        g_params->Set("Depth", g_depth);
        g_params->Set("MotionVectors", g_motion);
        g_params->Set("Output", g_output);

        g_feature = guarded([&]() { return create(g_list, NVSDK_NGX_Feature_SuperSampling, g_params, &g_handle); });
        return g_feature == NVSDK_NGX_Result_Success && g_handle != nullptr;
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
    // The exact Core build accepts the mapped ProjectID path when loaded from
    // KKS. Keep the standard path as a fallback for Feeder-compatible cores.
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
    else
        createTestFeature();
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

extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_LastFeatureResult()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return static_cast<unsigned int>(g_feature);
}

extern "C" __declspec(dllexport) void __cdecl KKS_DLSS12_Shutdown()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    releaseDevice();
    g_last = NVSDK_NGX_Result_FAIL_NotInitialized;
    g_feature = NVSDK_NGX_Result_FAIL_NotInitialized;
    g_appId = 0;
}

#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <mutex>
#include <string>
#include <vector>
#include <cstdio>
#include "third_party/DLSS/include/nvsdk_ngx.h"
#include "third_party/DLSS/include/nvsdk_ngx_params.h"
#include "third_party/DLSS/include/nvsdk_ngx_helpers.h"

namespace
{
    ID3D12Device* g_device = nullptr;
    NVSDK_NGX_Result g_last = NVSDK_NGX_Result_FAIL_NotInitialized;
    NVSDK_NGX_Result g_feature = NVSDK_NGX_Result_FAIL_NotInitialized;
    unsigned long long g_appId = 0;
    unsigned int g_attachCode = 0;
    unsigned int g_stageCodes[4]{};
    HRESULT g_stageHresults[4]{};
    ID3D12CommandQueue* g_queue = nullptr;
    IDXGIAdapter1* g_adapter12 = nullptr;
    ID3D12CommandAllocator* g_allocator = nullptr;
    ID3D12GraphicsCommandList* g_list = nullptr;
    ID3D12Fence* g_fence = nullptr;
    HANDLE g_fenceEvent = nullptr;
    unsigned long long g_fenceValue = 0;
    unsigned long long g_evalCount = 0;
    double g_lastEvalMs = 0.0;
    NVSDK_NGX_Parameter* g_params = nullptr;
    NVSDK_NGX_Handle* g_handle = nullptr;
    bool g_featureAttempted = false;
    bool g_firstEval = true;
    char g_capabilityReport[256] = "Capabilities not queried";
    ID3D12Resource* g_color = nullptr;
    ID3D12Resource* g_depth = nullptr;
    ID3D12Resource* g_motion = nullptr;
    ID3D12Resource* g_output = nullptr;
    ID3D11Device* g_d3d11 = nullptr;
    ID3D11DeviceContext* g_d3d11Context = nullptr;

    struct SharedSlot
    {
        ID3D11Texture2D* relay11 = nullptr;
        HANDLE handle = nullptr;
        ID3D12Resource* imported12 = nullptr;
        D3D11_TEXTURE2D_DESC desc{};
    };
    SharedSlot g_slots[4];
    std::mutex g_mutex;

    // D3D12 work can still be in flight when Unity destroys the image-effect
    // textures during a DLSS toggle.  Always drain our queue before releasing
    // NGX and the shared relay resources; otherwise the next initialization
    // can inherit an incomplete cross-API copy and present black frames.
    void waitForQueueIdle()
    {
        if (!g_queue || !g_fence || !g_fenceEvent)
            return;
        const unsigned long long value = ++g_fenceValue;
        if (FAILED(g_queue->Signal(g_fence, value)))
            return;
        if (FAILED(g_fence->SetEventOnCompletion(value, g_fenceEvent)))
            return;
        WaitForSingleObject(g_fenceEvent, 5000);
    }

    void releaseDevice()
    {
        // Drain submitted Evaluate/copy work before touching any resource it
        // may reference.  This is especially important for close -> reopen.
        waitForQueueIdle();

        // Release the NGX feature while its parameter-bound resources are
        // still alive.  The previous order released shared slots first,
        // leaving NGX with dangling resource references during teardown.
        if (g_handle)
        {
            auto release = &NVSDK_NGX_D3D12_ReleaseFeature;
            if (release) { __try { release(g_handle); } __except (EXCEPTION_EXECUTE_HANDLER) { } }
            g_handle = nullptr;
        }
        if (g_params)
        {
            auto destroy = &NVSDK_NGX_D3D12_DestroyParameters;
            if (destroy) { __try { destroy(g_params); } __except (EXCEPTION_EXECUTE_HANDLER) { } }
            g_params = nullptr;
        }

        for (auto& slot : g_slots)
        {
            if (slot.imported12) { slot.imported12->Release(); slot.imported12 = nullptr; }
            if (slot.handle) { CloseHandle(slot.handle); slot.handle = nullptr; }
            if (slot.relay11) { slot.relay11->Release(); slot.relay11 = nullptr; }
            slot.desc = {};
        }
        if (g_d3d11Context) { g_d3d11Context->Release(); g_d3d11Context = nullptr; }
        if (g_d3d11) { g_d3d11->Release(); g_d3d11 = nullptr; }
        g_featureAttempted = false;
        g_firstEval = true;
        g_evalCount = 0;
        g_lastEvalMs = 0.0;
        if (g_output) { g_output->Release(); g_output = nullptr; }
        if (g_motion) { g_motion->Release(); g_motion = nullptr; }
        if (g_depth) { g_depth->Release(); g_depth = nullptr; }
        if (g_color) { g_color->Release(); g_color = nullptr; }
        if (g_list) { g_list->Release(); g_list = nullptr; }
        if (g_fenceEvent) { CloseHandle(g_fenceEvent); g_fenceEvent = nullptr; }
        if (g_fence) { g_fence->Release(); g_fence = nullptr; }
        if (g_allocator) { g_allocator->Release(); g_allocator = nullptr; }
        if (g_queue) { g_queue->Release(); g_queue = nullptr; }
        if (g_adapter12) { g_adapter12->Release(); g_adapter12 = nullptr; }
        if (g_device)
        {
            auto shutdown = &NVSDK_NGX_D3D12_Shutdown1;
            if (shutdown)
            {
                __try { shutdown(g_device); }
                __except (EXCEPTION_EXECUTE_HANDLER) { }
            }
            g_device->Release();
            g_device = nullptr;
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
        if (g_featureAttempted)
            return g_feature == NVSDK_NGX_Result_Success && g_handle != nullptr;
        g_featureAttempted = true;

        auto allocate = &NVSDK_NGX_D3D12_GetCapabilityParameters;
        auto create = &NVSDK_NGX_D3D12_CreateFeature;
        if (!allocate || !create || !g_device) { g_feature = NVSDK_NGX_Result_FAIL_PlatformError; return false; }

        D3D12_COMMAND_QUEUE_DESC queueDesc{};
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        if (FAILED(g_device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&g_queue)))) { g_feature = NVSDK_NGX_Result_FAIL_PlatformError; return false; }
        if (FAILED(g_device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&g_allocator)))) { g_feature = NVSDK_NGX_Result_FAIL_PlatformError; return false; }
        if (FAILED(g_device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, g_allocator, nullptr, IID_PPV_ARGS(&g_list)))) { g_feature = NVSDK_NGX_Result_FAIL_PlatformError; return false; }

        g_feature = guarded([&]() { return allocate(&g_params); });
        if (g_feature != NVSDK_NGX_Result_Success || !g_params) return false;
        int available = -1, needsDriver = -1, initResult = 0;
        auto availableResult = g_params->Get(NVSDK_NGX_Parameter_SuperSampling_Available, &available);
        auto driverResult = g_params->Get(NVSDK_NGX_Parameter_SuperSampling_NeedsUpdatedDriver, &needsDriver);
        auto featureResult = g_params->Get(NVSDK_NGX_Parameter_SuperSampling_FeatureInitResult, &initResult);
        std::snprintf(g_capabilityReport, sizeof(g_capabilityReport),
            "SR available=%d (query=0x%08X), needsDriver=%d (query=0x%08X), initResult=0x%08X (query=0x%08X)",
            available, availableResult, needsDriver, driverResult, initResult, featureResult);

        const bool sharedInputs = g_slots[0].imported12 && g_slots[1].imported12 && g_slots[2].imported12;
        const bool sharedOutput = g_slots[3].imported12 != nullptr;
        const UINT width = sharedInputs ? g_slots[0].desc.Width : 1280;
        const UINT height = sharedInputs ? g_slots[0].desc.Height : 720;
        const UINT outputWidth = sharedOutput ? g_slots[3].desc.Width : width;
        const UINT outputHeight = sharedOutput ? g_slots[3].desc.Height : height;
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
        if ((!sharedInputs && FAILED(makeTexture(DXGI_FORMAT_R8G8B8A8_UNORM, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, &g_color))) ||
            (!sharedInputs && FAILED(makeTexture(DXGI_FORMAT_R32_FLOAT, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, &g_depth))) ||
            (!sharedInputs && FAILED(makeTexture(DXGI_FORMAT_R16G16_FLOAT, D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, &g_motion))) ||
            (!sharedOutput && FAILED(makeTexture(DXGI_FORMAT_R8G8B8A8_UNORM, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, &g_output))))
        {
            g_feature = NVSDK_NGX_Result_FAIL_PlatformError;
            return false;
        }

        g_params->Set("Width", width);
        g_params->Set("Height", height);
        g_params->Set("OutWidth", outputWidth);
        g_params->Set("OutHeight", outputHeight);
        g_params->Set("CreationNodeMask", (unsigned int)1);
        g_params->Set("VisibilityNodeMask", (unsigned int)1);
        // Use a real super-resolution mode. DLAA keeps input/output at the
        // same size and was the reason the first successful frames showed no
        // visible upscale effect.
        g_params->Set("PerfQualityValue", (int)NVSDK_NGX_PerfQuality_Value_MaxQuality);
        // KKS supplies live guide relays; creation flags include HDR and the
        // measured Unity reversed-Z depth convention.
        // Unity's D3D11 renderer uses a reversed-Z depth buffer on the KKS
        // runtime. Tell NGX explicitly so the captured depth guide is
        // interpreted with the correct near/far convention.
        const int createFlags = (int)(NVSDK_NGX_DLSS_Feature_Flags_IsHDR |
            NVSDK_NGX_DLSS_Feature_Flags_DepthInverted);
        g_params->Set("DLSS.Feature.Create.Flags", createFlags);
        g_params->Set("DLSS.Enable.Output.Subrects", (int)0);
        g_params->Set("Reset", (int)1);
        g_params->Set("Sharpness", 0.15f);
        g_params->Set("Jitter.Offset.X", 0.0f);
        g_params->Set("Jitter.Offset.Y", 0.0f);
        // Unity's _CameraMotionVectorsTexture stores normalized screen-space
        // displacement. NGX expects pixel-space displacement; the same
        // conversion used by Unity's MotionBlur pass is v * 0.5 * (w, h).
        const float mvScaleX = width * 0.5f;
        const float mvScaleY = height * 0.5f;
        g_params->Set("MV.Scale.X", mvScaleX);
        g_params->Set("MV.Scale.Y", mvScaleY);
        g_params->Set("Color", sharedInputs ? g_slots[0].imported12 : g_color);
        g_params->Set("Depth", sharedInputs ? g_slots[1].imported12 : g_depth);
        g_params->Set("MotionVectors", sharedInputs ? g_slots[2].imported12 : g_motion);
        g_params->Set("Output", sharedOutput ? g_slots[3].imported12 : g_output);
        std::snprintf(g_capabilityReport, sizeof(g_capabilityReport),
            "SR available=%d, needsDriver=%d, initResult=0x%08X; NGX input=%ux%u output=%ux%u mode=MaxQuality sharpness=0.15 flags=0x%X MVScale=%.1fx%.1f",
            available, needsDriver, initResult, width, height, outputWidth, outputHeight, createFlags,
            mvScaleX, mvScaleY);

        g_feature = guarded([&]() { return create(g_list, NVSDK_NGX_Feature_SuperSampling, g_params, &g_handle); });
        if (g_feature != NVSDK_NGX_Result_Success || !g_handle) return false;
        HRESULT hr = g_list->Close();
        if (SUCCEEDED(hr)) hr = g_device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&g_fence));
        if (SUCCEEDED(hr)) {
            g_fenceEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
            if (!g_fenceEvent) hr = HRESULT_FROM_WIN32(GetLastError());
        }
        if (SUCCEEDED(hr)) {
            ID3D12CommandList* lists[] = {g_list};
            g_queue->ExecuteCommandLists(1, lists);
            hr = g_queue->Signal(g_fence, ++g_fenceValue);
        }
        if (SUCCEEDED(hr)) hr = g_fence->SetEventOnCompletion(g_fenceValue, g_fenceEvent);
        if (SUCCEEDED(hr) && WaitForSingleObject(g_fenceEvent, 5000) != WAIT_OBJECT_0)
            hr = HRESULT_FROM_WIN32(ERROR_TIMEOUT);
        if (FAILED(hr)) {
            g_feature = NVSDK_NGX_Result_FAIL_PlatformError;
            return false;
        }
        return true;
    }

    bool evaluateTestFrame()
    {
        if (!g_handle || !g_params || !g_queue || !g_allocator || !g_list ||
            !g_slots[0].imported12 || !g_slots[1].imported12 || !g_slots[2].imported12 ||
            (!g_slots[3].imported12 && !g_output))
            return false;
        if (FAILED(g_allocator->Reset()) || FAILED(g_list->Reset(g_allocator, nullptr))) return false;
        LARGE_INTEGER tickStart{}, tickEnd{}, frequency{};
        QueryPerformanceFrequency(&frequency);
        QueryPerformanceCounter(&tickStart);
        NVSDK_NGX_D3D12_DLSS_Eval_Params eval{};
        eval.Feature.InSharpness = 0.15f;
        eval.Feature.pInColor = g_slots[0].imported12;
        eval.Feature.pInOutput = g_slots[3].imported12 ? g_slots[3].imported12 : g_output;
        eval.pInDepth = g_slots[1].imported12;
        eval.pInMotionVectors = g_slots[2].imported12;
        eval.InRenderSubrectDimensions.Width = g_slots[0].desc.Width;
        eval.InRenderSubrectDimensions.Height = g_slots[0].desc.Height;
        eval.InMVScaleX = g_slots[0].desc.Width * 0.5f;
        eval.InMVScaleY = g_slots[0].desc.Height * 0.5f;
        eval.InFrameTimeDeltaInMsec = 16.667f;
        // Reset history only for the first frame after feature creation. A
        // permanent reset would disable temporal accumulation and make the
        // motion-vector input effectively useless.
        eval.InReset = g_firstEval ? 1 : 0;
        eval.InToneMapperType = NVSDK_NGX_TONEMAPPER_STRING;
        NVSDK_NGX_Result r = NGX_D3D12_EVALUATE_DLSS_EXT(g_list, g_handle, g_params, &eval);
        if (r != NVSDK_NGX_Result_Success || FAILED(g_list->Close())) { g_feature = r; return false; }
        ID3D12CommandList* lists[] = {g_list}; g_queue->ExecuteCommandLists(1, lists);
        if (FAILED(g_queue->Signal(g_fence, ++g_fenceValue)) || FAILED(g_fence->SetEventOnCompletion(g_fenceValue, g_fenceEvent))) return false;
        bool completed = WaitForSingleObject(g_fenceEvent, 5000) == WAIT_OBJECT_0;
        QueryPerformanceCounter(&tickEnd);
        if (completed)
        {
            g_firstEval = false;
            ++g_evalCount;
            g_lastEvalMs = frequency.QuadPart ?
                (1000.0 * static_cast<double>(tickEnd.QuadPart - tickStart.QuadPart) / static_cast<double>(frequency.QuadPart)) : 0.0;
        }
        return completed;
    }

    bool ensureSharedSlot(unsigned int slotIndex, ID3D11Texture2D* source)
    {
        if (slotIndex >= 4 || !source || !g_d3d11 || !g_device) { if (slotIndex < 4) g_stageCodes[slotIndex] = 1; return false; }
        D3D11_TEXTURE2D_DESC sourceDesc{};
        source->GetDesc(&sourceDesc);
        SharedSlot& slot = g_slots[slotIndex];
        bool same = slot.relay11 && slot.desc.Width == sourceDesc.Width && slot.desc.Height == sourceDesc.Height && slot.desc.Format == sourceDesc.Format && slot.desc.ArraySize == sourceDesc.ArraySize;
        if (!same)
        {
            if (slot.imported12) { slot.imported12->Release(); slot.imported12 = nullptr; }
            if (slot.handle) { CloseHandle(slot.handle); slot.handle = nullptr; }
            if (slot.relay11) { slot.relay11->Release(); slot.relay11 = nullptr; }
            D3D11_TEXTURE2D_DESC relayDesc = sourceDesc;
            relayDesc.MipLevels = 1;
            relayDesc.ArraySize = 1;
            relayDesc.Usage = D3D11_USAGE_DEFAULT;
            // NGX writes the output through an unordered-access view. Unity's
            // output RT is created with random-write enabled, but the relay
            // descriptor must carry the UAV bind flag as well or the imported
            // D3D12 resource is rejected with RWFlagMissing (0xBAD00009).
            relayDesc.BindFlags = slotIndex == 3 ? D3D11_BIND_UNORDERED_ACCESS : 0;
            relayDesc.CPUAccessFlags = 0;
            relayDesc.SampleDesc.Count = 1;
            relayDesc.SampleDesc.Quality = 0;
            relayDesc.MiscFlags = D3D11_RESOURCE_MISC_SHARED_NTHANDLE | D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;
            HRESULT createHr = g_d3d11->CreateTexture2D(&relayDesc, nullptr, &slot.relay11);
            if (FAILED(createHr)) { g_stageHresults[slotIndex] = createHr; g_stageCodes[slotIndex] = 3; return false; }
            IDXGIResource1* dxgiResource = nullptr;
            if (FAILED(slot.relay11->QueryInterface(IID_PPV_ARGS(&dxgiResource)))) { g_stageCodes[slotIndex] = 4; return false; }
            HRESULT hr = dxgiResource->CreateSharedHandle(nullptr, GENERIC_ALL, nullptr, &slot.handle);
            dxgiResource->Release();
            if (FAILED(hr) || !slot.handle) { g_stageCodes[slotIndex] = 5; return false; }
            if (FAILED(g_device->OpenSharedHandle(slot.handle, IID_PPV_ARGS(&slot.imported12)))) { g_stageCodes[slotIndex] = 6; return false; }
            slot.desc = sourceDesc;
        }
        // Slot 3 is the DLSS output relay; it is written by NGX and must not
        // be overwritten with the pre-existing Unity target contents.
        if (slotIndex != 3)
        {
            if (sourceDesc.SampleDesc.Count > 1)
                g_d3d11Context->ResolveSubresource(slot.relay11, 0, source, 0, sourceDesc.Format);
            else
                g_d3d11Context->CopyResource(slot.relay11, source);
        }
        g_d3d11Context->Flush();
        g_stageCodes[slotIndex] = 8;
        if (!g_handle && g_slots[0].imported12 && g_slots[1].imported12 && g_slots[2].imported12 && g_slots[3].imported12)
            createTestFeature();
        return true;
    }
}

extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_Init(const wchar_t* appDataPath)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    releaseDevice();
    g_feature = NVSDK_NGX_Result_FAIL_NotInitialized;
    std::snprintf(g_capabilityReport, sizeof(g_capabilityReport), "Capabilities not queried");

    const wchar_t* path = appDataPath ? appDataPath : L".";
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
    if (SUCCEEDED(hr) && adapter) { adapter->AddRef(); g_adapter12 = adapter; }
    if (adapter) adapter->Release();
    factory->Release();
    if (FAILED(hr) || !g_device)
    {
        g_last = NVSDK_NGX_Result_FAIL_PlatformError;
        releaseDevice();
        return static_cast<unsigned int>(g_last);
    }

    const wchar_t* runtimePaths[] = {path};
    NVSDK_NGX_FeatureCommonInfo info{};
    info.PathListInfo.Path = runtimePaths;
    info.PathListInfo.Length = 1;
    g_last = guarded([&]() { return NVSDK_NGX_D3D12_Init_with_ProjectID(
        "6a9c4c0d-6f1c-4c4d-9a70-6f2d0f4f2c19", NVSDK_NGX_ENGINE_TYPE_CUSTOM,
        "1.0", path, g_device, &info, NVSDK_NGX_Version_API); });

    if (g_last != NVSDK_NGX_Result_Success)
        releaseDevice();
    // Feature creation is intentionally deferred until the live D3D11 color,
    // depth, and motion-vector relays have all been staged.
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

// Isolated creation test only; never evaluates or presents a synthetic frame.
extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_ProbeFeature()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_last == NVSDK_NGX_Result_Success) createTestFeature();
    return static_cast<unsigned int>(g_feature);
}

extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_ProbeEvaluate()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return evaluateTestFrame() ? 1u : 0u;
}

extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_CopyOutputToD3D11(void* target)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!target || !g_d3d11Context || !g_slots[3].relay11) return 0;
    ID3D11Resource* dst = reinterpret_cast<ID3D11Resource*>(target);
    D3D11_RESOURCE_DIMENSION dim{}; dst->GetType(&dim);
    if (dim != D3D11_RESOURCE_DIMENSION_TEXTURE2D) return 0;
    g_d3d11Context->CopyResource(dst, g_slots[3].relay11);
    g_d3d11Context->Flush();
    return 1;
}

extern "C" __declspec(dllexport) unsigned long long __cdecl KKS_DLSS12_EvalCount()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_evalCount;
}

extern "C" __declspec(dllexport) double __cdecl KKS_DLSS12_LastEvalMilliseconds()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_lastEvalMs;
}

extern "C" __declspec(dllexport) const char* __cdecl KKS_DLSS12_CapabilityReport()
{
    return g_capabilityReport;
}

extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_AttachD3D11(void* device, void* context)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    g_attachCode = 0;
    if (!device || !context || !g_device) { g_attachCode = 1; return 0; }
    ID3D11Device* d3d11 = reinterpret_cast<ID3D11Device*>(device);
    ID3D11DeviceContext* d3d11Context = reinterpret_cast<ID3D11DeviceContext*>(context);
    IDXGIDevice* dxgi11 = nullptr;
    IDXGIAdapter* adapter11 = nullptr;
    IDXGIAdapter* adapter12 = nullptr;
    DXGI_ADAPTER_DESC desc11{};
    DXGI_ADAPTER_DESC desc12{};
    bool matched = false;
    if (FAILED(d3d11->QueryInterface(IID_PPV_ARGS(&dxgi11)))) { g_attachCode = 2; goto done; }
    if (FAILED(dxgi11->GetAdapter(&adapter11))) { g_attachCode = 3; goto done; }
    if (FAILED(adapter11->GetDesc(&desc11))) { g_attachCode = 4; goto done; }
    if (!g_adapter12) { g_attachCode = 5; goto done; }
    g_adapter12->AddRef();
    adapter12 = g_adapter12;
    if (FAILED(adapter12->GetDesc(&desc12))) { g_attachCode = 6; goto done; }
    matched = desc11.AdapterLuid.LowPart == desc12.AdapterLuid.LowPart && desc11.AdapterLuid.HighPart == desc12.AdapterLuid.HighPart;
    if (!matched) g_attachCode = 7;
    if (matched)
    {
        d3d11->AddRef();
        d3d11Context->AddRef();
        if (g_d3d11Context) g_d3d11Context->Release();
        if (g_d3d11) g_d3d11->Release();
        g_d3d11 = d3d11;
        g_d3d11Context = d3d11Context;
    }
done:
    if (adapter12) adapter12->Release();
    if (adapter11) adapter11->Release();
    if (dxgi11) dxgi11->Release();
    if (matched) g_attachCode = 8;
    return matched ? 1u : 0u;
}

extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_LastAttachCode()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_attachCode;
}

extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_StageD3D11Texture(unsigned int slot, void* resource)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_d3d11 || !g_d3d11Context || slot >= 4 || !resource) { if (slot < 4) g_stageCodes[slot] = 1; return 0; }
    ID3D11Texture2D* source = nullptr;
    ID3D11Resource* sourceResource = reinterpret_cast<ID3D11Resource*>(resource);
    if (FAILED(sourceResource->QueryInterface(IID_PPV_ARGS(&source)))) { g_stageCodes[slot] = 2; return 0; }
    bool ok = ensureSharedSlot(slot, source);
    source->Release();
    return ok ? 1u : 0u;
}

extern "C" __declspec(dllexport) unsigned int __cdecl KKS_DLSS12_LastStageCode(unsigned int slot)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return slot < 4 ? g_stageCodes[slot] : 1u;
}

extern "C" __declspec(dllexport) int __cdecl KKS_DLSS12_LastStageHRESULT(unsigned int slot)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return slot < 4 ? static_cast<int>(g_stageHresults[slot]) : 0;
}

extern "C" __declspec(dllexport) void* __cdecl KKS_DLSS12_GetD3D12Texture(unsigned int slot)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    if (slot >= 4) return nullptr;
    return g_slots[slot].imported12;
}

extern "C" __declspec(dllexport) void __cdecl KKS_DLSS12_Shutdown()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    releaseDevice();
    g_last = NVSDK_NGX_Result_FAIL_NotInitialized;
    g_feature = NVSDK_NGX_Result_FAIL_NotInitialized;
    g_appId = 0;
}

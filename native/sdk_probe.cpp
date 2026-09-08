#include <windows.h>
#include <d3d12.h>
#include <wrl/client.h>
#include <cstdio>
#include "third_party/DLSS/include/nvsdk_ngx.h"
#include "third_party/DLSS/include/nvsdk_ngx_helpers.h"

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) { std::printf("Usage: sdk_probe runtime-directory\n"); return 2; }
    Microsoft::WRL::ComPtr<ID3D12Device> device;
    HRESULT hr = D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device));
    if (FAILED(hr)) { std::printf("device=0x%08X\n", hr); return 3; }
    const wchar_t* paths[] = {argv[1]};
    NVSDK_NGX_FeatureCommonInfo info{};
    info.PathListInfo.Path = paths;
    info.PathListInfo.Length = 1;
    auto result = NVSDK_NGX_D3D12_Init_with_ProjectID(
        "6a9c4c0d-6f1c-4c4d-9a70-6f2d0f4f2c19", NVSDK_NGX_ENGINE_TYPE_CUSTOM,
        "1.0", argv[1], device.Get(), &info, NVSDK_NGX_Version_API);
    std::printf("SDK init=0x%08X\n", result);
    if (NVSDK_NGX_FAILED(result)) return 4;
    NVSDK_NGX_Parameter* caps = nullptr;
    result = NVSDK_NGX_D3D12_GetCapabilityParameters(&caps);
    int available = -1, featureInit = 0;
    if (NVSDK_NGX_SUCCEED(result) && caps)
    {
        caps->Get(NVSDK_NGX_Parameter_SuperSampling_Available, &available);
        caps->Get(NVSDK_NGX_Parameter_SuperSampling_FeatureInitResult, &featureInit);
    }
    std::printf("capabilities=0x%08X available=%d featureInit=0x%08X\n", result, available, featureInit);
    Microsoft::WRL::ComPtr<ID3D12CommandAllocator> allocator;
    Microsoft::WRL::ComPtr<ID3D12GraphicsCommandList> list;
    NVSDK_NGX_Handle* handle = nullptr;
    bool created = false;
    if (available == 1 && SUCCEEDED(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator))) &&
        SUCCEEDED(device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator.Get(), nullptr, IID_PPV_ARGS(&list))))
    {
        NVSDK_NGX_DLSS_Create_Params create{};
        create.Feature.InWidth = 1280;
        create.Feature.InHeight = 720;
        create.Feature.InTargetWidth = 1920;
        create.Feature.InTargetHeight = 1080;
        create.Feature.InPerfQualityValue = NVSDK_NGX_PerfQuality_Value_MaxQuality;
        create.InFeatureCreateFlags = NVSDK_NGX_DLSS_Feature_Flags_MVLowRes | NVSDK_NGX_DLSS_Feature_Flags_AutoExposure;
        result = NGX_D3D12_CREATE_DLSS_EXT(list.Get(), 1, 1, &handle, caps, &create);
        std::printf("SDK CreateFeature=0x%08X handle=%p (not evaluated)\n", result, handle);
        created = NVSDK_NGX_SUCCEED(result) && handle;
    }
    if (handle) NVSDK_NGX_D3D12_ReleaseFeature(handle);
    if (caps) NVSDK_NGX_D3D12_DestroyParameters(caps);
    NVSDK_NGX_D3D12_Shutdown1(device.Get());
    return created ? 0 : 5;
}

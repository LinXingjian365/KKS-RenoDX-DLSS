#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <cstdio>
#include <string>
#include "third_party/DLSS/include/nvsdk_ngx.h"

static void *sym(HMODULE m, const char *name) { return reinterpret_cast<void *>(GetProcAddress(m, name)); }

int main()
{
    // The public NGX entry points live in the driver-provided core module.
    // nvngx_dlss.dll is the feature implementation loaded by that core.
    HMODULE ngx = LoadLibraryW(L"_nvngx.dll");
    if (!ngx) { std::printf("LOAD_NGX failed %lu\n", GetLastError()); return 2; }

    IDXGIFactory6 *factory = nullptr;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(hr)) { std::printf("FACTORY hr=0x%08X\n", (unsigned)hr); return 3; }

    IDXGIAdapter1 *adapter = nullptr;
    for (UINT i = 0; factory->EnumAdapterByGpuPreference(i, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, IID_PPV_ARGS(&adapter)) != DXGI_ERROR_NOT_FOUND; ++i)
    {
        DXGI_ADAPTER_DESC1 desc{};
        adapter->GetDesc1(&desc);
        if (!(desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE)) break;
        adapter->Release(); adapter = nullptr;
    }
    ID3D12Device *device = nullptr;
    hr = D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device));
    std::printf("D3D12CreateDevice hr=0x%08X device=%p\n", (unsigned)hr, device);
    if (FAILED(hr)) return 4;

    using InitFn = NVSDK_NGX_Result (NVSDK_CONV *)(unsigned long long, const wchar_t *, ID3D12Device *, NVSDK_NGX_Version);
    using ProjectInitFn = NVSDK_NGX_Result (NVSDK_CONV *)(const char *, NVSDK_NGX_EngineType, const char *, const wchar_t *, ID3D12Device *, const NVSDK_NGX_FeatureCommonInfo *, NVSDK_NGX_Version);
    auto init = reinterpret_cast<InitFn>(sym(ngx, "NVSDK_NGX_D3D12_Init"));
    auto projectInit = reinterpret_cast<ProjectInitFn>(sym(ngx, "NVSDK_NGX_D3D12_Init_ProjectID"));
    const wchar_t *path = L"D:\\Koikatsu Sunshine\\";
    NVSDK_NGX_Result result = NVSDK_NGX_Result_FAIL_PlatformError;
    if (projectInit)
        result = projectInit("6a9c4c0d-6f1c-4c4d-9a70-6f2d0f4f2c19", NVSDK_NGX_ENGINE_TYPE_CUSTOM, "Unity 2019.4.9f1 KKS", path, device, nullptr, NVSDK_NGX_Version_API);
    else if (init)
        result = init(0, path, device, NVSDK_NGX_Version_API);
    std::printf("projectInit=%p init=%p result=0x%08X\n", (void *)projectInit, (void *)init, (unsigned)result);

    if (result == NVSDK_NGX_Result_Success)
    {
        using ShutdownFn = NVSDK_NGX_Result (NVSDK_CONV *)(ID3D12Device *);
        auto shutdown = reinterpret_cast<ShutdownFn>(sym(ngx, "NVSDK_NGX_D3D12_Shutdown1"));
        if (shutdown) shutdown(device);
    }
    device->Release();
    if (adapter) adapter->Release();
    factory->Release();
    return result == NVSDK_NGX_Result_Success ? 0 : 5;
}

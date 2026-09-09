#include <windows.h>
#include <cstdio>

using InitFn = unsigned int (__cdecl *)(const wchar_t*);
using LastFn = unsigned int (__cdecl *)();
using AppIdFn = unsigned long long (__cdecl *)();
using FeatureFn = unsigned int (__cdecl *)();
using ShutdownFn = void (__cdecl *)();

int wmain(int argc, wchar_t** argv)
{
    HMODULE bridge = LoadLibraryW(L"kks_dlss_d3d12_bridge.dll");
    if (!bridge)
    {
        std::printf("bridge load failed: %lu\n", GetLastError());
        return 2;
    }
    auto init = reinterpret_cast<InitFn>(GetProcAddress(bridge, "KKS_DLSS12_Init"));
    auto last = reinterpret_cast<LastFn>(GetProcAddress(bridge, "KKS_DLSS12_LastResult"));
    auto appId = reinterpret_cast<AppIdFn>(GetProcAddress(bridge, "KKS_DLSS12_LastAppId"));
    auto feature = reinterpret_cast<FeatureFn>(GetProcAddress(bridge, "KKS_DLSS12_LastFeatureResult"));
    auto shutdown = reinterpret_cast<ShutdownFn>(GetProcAddress(bridge, "KKS_DLSS12_Shutdown"));
    auto probe = reinterpret_cast<FeatureFn>(GetProcAddress(bridge, "KKS_DLSS12_ProbeFeature"));
    auto evaluate = reinterpret_cast<FeatureFn>(GetProcAddress(bridge, "KKS_DLSS12_ProbeEvaluate"));
    auto report = reinterpret_cast<const char* (__cdecl *)()>(GetProcAddress(bridge, "KKS_DLSS12_CapabilityReport"));
    if (!init || !last || !appId || !feature || !shutdown)
    {
        std::printf("bridge exports missing\n");
        FreeLibrary(bridge);
        return 3;
    }
    unsigned int result = init(argc > 1 ? argv[1] : L"D:\\Koikatsu Sunshine\\");
    std::printf("init result=0x%08X last=0x%08X appId=0x%llX feature=0x%08X\n", result, last(), appId(), feature());
    unsigned int created = result == 1 && probe ? probe() : 0;
    std::printf("isolated CreateFeature=0x%08X (no evaluation/output test)\n", created);
    std::printf("isolated Evaluate=%s (requires staged live textures)\n", evaluate && evaluate() ? "attempted" : "not available");
    if (report) std::printf("%s\n", report());
    shutdown();
    FreeLibrary(bridge);
    return result != 1 ? 4 : created == 1 ? 0 : 5;
}

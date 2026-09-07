#include <windows.h>
#include <cstdio>

using InitFn = unsigned int (__cdecl *)(const wchar_t*);
using LastFn = unsigned int (__cdecl *)();
using AppIdFn = unsigned long long (__cdecl *)();
using FeatureFn = unsigned int (__cdecl *)();
using ShutdownFn = void (__cdecl *)();

int main()
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
    if (!init || !last || !appId || !feature || !shutdown)
    {
        std::printf("bridge exports missing\n");
        FreeLibrary(bridge);
        return 3;
    }
    unsigned int result = init(L"D:\\Koikatsu Sunshine\\");
    std::printf("init result=0x%08X last=0x%08X appId=0x%llX feature=0x%08X\n", result, last(), appId(), feature());
    shutdown();
    FreeLibrary(bridge);
    return result == 1 ? 0 : 4;
}

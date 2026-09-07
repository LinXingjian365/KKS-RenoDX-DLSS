# Development Log

## 2026-09-05: Native DLSS investigation and verified Feeder route

### 2026-09-07: Strict runtime acceptance gate

The current Feeder log proves that NGX initializes, creates a Super Resolution feature, and delivers multiple frames. It also reports a flat depth probe and nearly-zero motion vectors. The project therefore records two separate outcomes: transport/evaluation PASS, strict temporal-input quality FAIL. `tools/verify_dlss_log.ps1` makes this distinction reproducible and exits non-zero until both guides contain useful scene data.

Research also confirmed the boundary of `dlss5-bridge`: mirror mode is designed for an existing native DLSS request, while synthetic mode is a substitute built from ReShade depth and optical flow. It is not an exact KKS-native input path. The native milestone remains a private D3D12 NGX bridge with real KKS color/depth/MV/jitter/exposure capture and synchronized output copy-back.

The native KKS test then confirmed a separate blocker: Unity D3D11 device/context discovery and guide-capture installation succeed, but the installed NGX runtime has no `NVSDK_NGX_D3D11_Init_with_ProjectID` export and every remaining D3D11 initialization attempt returns `PlatformError`. Automatic retry is now terminally suppressed; the implementation must move to the private D3D12 bridge.

The first private D3D12 bridge probe now builds and runs in an isolated process. It creates a valid D3D12 device and resolves `NVSDK_NGX_D3D12_Init_ProjectID`, but the test ProjectID returns `0xBAD00002` (`PlatformError`). No game files are touched by this probe. The next native milestone is therefore valid NGX project/application identity plus shared D3D11/D3D12 resource synchronization, not another D3D11 retry.

The bridge was corrected to load the exact KKS-side `_nvngx.dll` and guard NGX entry points with SEH. The standard D3D12 init now succeeds with diagnostic AppID `0`. This removes the identity blocker for the private session; it does not yet claim DLSS output because no feature or shared texture path is wired yet.

The bridge now also allocates a D3D12 queue/list and attempts a SuperSampling feature with test resources. NGX returns `0xBAD0000B` (`UnableToInitializeFeature`), so this is a feature-contract failure, not a transport failure. The next implementation step is to replace dummy resources with KKS-shared textures and match the exact Feeder/NVIDIA creation contract.

### Failed native approaches

1. **BepInEx `PPE_DLSS.dll`**: camera discovery and managed initialization worked, but KKS did not provide the color/depth/motion-vector contract and command-queue ownership required by NGX. No reliable native `CreateFeature`/`EvaluateFeature` pair was produced.
2. **ShortFuse `renodx-dlss.addon64`**: ReShade loaded the add-on and Present/D3D12 proxy hooks were visible, but KKS made no native DLSS request.
3. **`dlss5-bridge.addon64` + RenoDX DLSS5**: the bridge is for games that already call DLSS; KKS has no call to bridge.
4. **`nvngx_dlssnr.dll` alone**: feature 18 is a neural-rendering consumer, not a replacement for SuperSampling.
5. **HDRP/URP replacement**: KKS depends on Unity Built-in shaders and Studio render controls. Editor HDRP assets cannot be assigned to the player as a drop-in renderer.

### Working architecture

```text
KKS D3D11 frame
  -> ReShade Generic Depth + LumeniteFX Kernel
  -> DLSS5_Feed synthetic contract
  -> RenoDX DLSS5 neural consumer
  -> NVIDIA NGX SuperSampling
  -> 1920x1080 output
```

Observed proof:

```text
NVSDK_NGX_D3D12_Init -> 0x00000001 (Success)
DLSS Quality at 1920x1080: optimal 1280x720
feature ready: 1288x724 -> 1920x1080 DLSS Quality (synthetic jitter)
frame 1 delivered (..., DLSS SR, ...)
```

### Preserved lessons

- Never call a DLL load or overlay toggle “working DLSS”.
- Separate DLSSNR feature 18 from DLSS SuperSampling.
- Keep only one Feeder/bridge/replacement route active.
- Use the NVIDIA driver `nvngx.dll`/`_nvngx.dll` when package copies fail NGX initialization.
- Pin a known-good driver/add-on combination; closed-source NGX behavior can change with drivers.
- Use motion-vector and depth probes as independent quality checks.
- A flat depth probe is a quality failure even when DLSS SR frames are delivered.
- Do not promise HDRP/URP runtime parity for a Built-in Unity player without rewriting its renderer and shader ecosystem.

### Current status

- DLSS Super Resolution contract: verified.
- RenoDX DLSS5 neural pass: verified.
- Lumenite motion vectors: verified non-zero.
- Generic Depth scene selection: one-time manual runtime step remains.
- Frame generation: not enabled.
- Proprietary NVIDIA/ReShade/Discord binaries: not redistributed by this repository.

## 2026-09-05: MSAA isolation and native-input audit

KKS hardware/post-process antialiasing was disabled and Studio was restarted. DLSS5-Feeder continued to produce genuine DLSS SR frames, but its depth probe stayed flat. The failure is therefore not caused by KKS MSAA.

An audit of the native `PPE_DLSS` source found that it currently creates an empty depth texture and a zeroed motion-vector texture, then submits only a final color copy to NGX. That is a proof-of-concept shell, not a native-input implementation. The correct next native milestone is to capture KKS's real scene depth and motion data, bind them with the official NGX parameter names/flags, and synchronize the D3D11 output before presenting it.

The first implementation step is now in `src/PPE_DLSS_Plugin.cs` and `src/DLSSWrapper.cs`: the native experiment reads Unity's `_CameraDepthTexture` and `_CameraMotionVectorsTexture` at evaluate time and binds their native resource pointers to NGX. It builds cleanly and is deployed with the toggle still off. This is an input-capture milestone, not yet a claim of a complete native output path; resolution scaling, depth/MV resampling, jitter, and D3D11 synchronization still require validation.

The isolated native run then reached the real KKS `BackCamera` and obtained valid D3D11 device/context pointers, but every D3D11 NGX initialization attempt returned `PlatformError`. The custom ProjectID fallback could not be used because the installed `nvngx_dlss.dll` does not export `NVSDK_NGX_D3D11_Init_with_ProjectID`. This proves the remaining blocker is the D3D11 NGX runtime/initialization path. The next native milestone is a D3D12 device bridge, not more ReShade depth tuning.

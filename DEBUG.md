# KKS DLSS Debug Record

This file records the failed native experiments and the verified workaround. A loaded DLL or visible toggle is not treated as proof that DLSS is rendering.

## Strict acceptance rule

The repository now separates two claims:

- **Transport/evaluation success:** `session ready`, `feature ready`, and at least three `frame N delivered` lines.
- **Temporal-input quality success:** the same markers plus non-flat scene depth and non-zero motion vectors.

Run `tools/verify_dlss_log.ps1`. The current KKS log passes the first claim and fails the second. This is intentional: NGX is running, but the guides are not yet trustworthy enough to call the result accurate native DLSS.

## Environment

- Koikatsu Sunshine CharaStudio
- Unity 2019.4.9f1, 64-bit, D3D11, forward renderer
- RTX 3060 Laptop GPU
- Verified NVIDIA driver: 616.56
- ReShade 6.8 with add-on support

## Root cause: why the native BepInEx DLSS path cannot work reliably

KKS does not create the complete DLSS feature contract. A real DLSS integration needs a render/output resolution pair, a color resource, scene depth with correct flags, temporal motion vectors, jitter history, a D3D12 device/queue/command-list path, and an output copy-back. KKS's Built-in/Forward renderer exposes none of this as a native DLSS request.

Therefore:

- `PPE_DLSS.dll` can load and accept `Ctrl+D` without creating a real SuperSampling feature.
- ShortFuse `renodx-dlss.addon64` can hook Present and create a D3D12 proxy while remaining idle because no game `CreateFeature`/`EvaluateFeature` call exists.
- `dlss5-bridge.addon64` is for a DX11 game that already emits a DLSS contract; it does not invent one for KKS.
- `nvngx_dlssnr.dll` backs DLSSNR/neural-rendering feature 18. It is not, by itself, DLSS Super Resolution.

The working route uses DLSS5-Feeder to create the missing request from ReShade color, depth, and motion-vector textures, then lets RenoDX DLSS5 consume it.

## Failure ledger

### Native plugin says initialized, but the image does not change

Initialization only proves that managed code found a camera and loaded a native library. It does not prove NGX feature creation or evaluation. Require `CreateFeature`, `EvaluateFeature`, and a replaced output frame.

### RenoDX ShortFuse loads but no upscaling occurs

The KKS test reached `init_device`, `init_swapchain`, and `first present`, but no native DLSS feature was created. Use either the standalone ShortFuse route or the Feeder route, never both. For KKS, the verified route is Feeder + RenoDX DLSS5.

### DLSSNR is visible but this is not DLSS SR

Feature 18 is the neural-rendering consumer. Verify `SuperSampling -> supported`, `NVSDK_NGX_D3D12_Init -> 0x00000001`, `feature ready: ... DLSS Quality`, and `DLSS SR` frame lines.

### `0xBAD00002`, `HashMismatch`, or NGX validation failure

These indicate an incompatible or corrupted `nvngx_dlssnr.dll`/runtime pair. The old Streamline package used in testing produced a runtime validation failure. Keep the tested runtime pair and use the current NVIDIA driver `nvngx.dll`/`_nvngx.dll`. Do not patch or re-sign NVIDIA binaries. This repository does not redistribute them.

### `0xBAD00012` for feature 18

The optional feature-18 capability query is not implemented by that runtime. It is not fatal when normal SuperSampling is supported and the RenoDX consumer intercepts the synthetic contract.

### Driver 616.64 / RenoDX DLSS5 v4.6+ evaluation crash

The Feeder release notes document an evaluation fault on driver 616.64 with newer RenoDX DLSS5 builds, while 616.56 passed the same test matrix. The tested KKS machine is pinned to 616.56 until upstream compatibility changes. See the [Feeder release notes](https://github.com/jlrouzies-fr/DLSS5-Feeder).

### DLSS runs at 100% but performance is poor

`work_resolution=100` is DLAA, not an upscale. The verified Quality test uses:

```ini
work_resolution=67
work_upscale=2
```

`work_upscale=0` only stretches the reduced work image. `work_upscale=2` is the Feeder's experimental synthetic-jitter DLSS Quality path and produced `DLSS SR` in KKS.

### Depth probe is flat

`Depth probe ... min 0, max 0, variance 0` means ReShade Generic Depth selected a cleared/UI resource. DLSS can still deliver frames, but temporal reconstruction has no usable scene depth.

Open ReShade `Home -> Add-ons -> Generic Depth`, enable the copy-before-clear options, then select the resource whose preview contains the 3D scene. The resource handle is created at runtime and cannot be safely hard-coded. See the [ReShade Generic Depth implementation](https://github.com/crosire/reshade/blob/main/examples/09-depth/generic_depth_addon.cpp).

### MSAA-off test on KKS

The active KKS settings were tested with both paths disabled:

```ini
org.bepinex.plugins.KKS_PostProcessingEffectsV3.cfg:
AntiAliasing Mode = None

keelhauled.graphicssettings.cfg:
Anti-aliasing multiplier = Disabled
```

After restart, the Feeder still reported `feature ready ... DLSS Quality`, `DLSS SR`, and non-zero motion vectors, while the depth probe remained flat. This isolates the current depth failure from MSAA. The files were backed up under `D:\Koikatsu Sunshine\_codex_archive\DLSS\msaa_off_20260905_164527`.

### Native D3D11 isolation result

The native test was run with Feeder/RenoDX add-ons removed from the active directory and the native toggle enabled. KKS reached `BackCamera` and reported valid D3D11 device/context pointers. NGX initialization then returned `PlatformError` for all tested application IDs and paths. The official custom ProjectID fallback was also attempted, but the installed runtime did not export `NVSDK_NGX_D3D11_Init_with_ProjectID`.

This is why the native route cannot be declared complete yet. The D3D11 KKS device is valid, but this runtime combination does not accept the in-process D3D11 NGX initialization. The primary route must therefore move the NGX session to a private D3D12 device and bridge Unity's resources, which is the same architectural class as the already-working Feeder but implemented inside the native project.

### Why the current native `PPE_DLSS.dll` is not yet a native-input implementation

The repository's experimental source was useful for proving the failure mode, but it is not ready for release as a native solution:

- `DLSSWrapper.CreateTextures()` creates a new depth texture but never copies KKS scene depth into it.
- The motion-vector texture is explicitly cleared to zero.
- `OnRenderImage` copies only the final color image; it does not capture KKS's pre-tonemap scene color, depth, exposure, or jittered camera state.
- `ScalableBufferManager.ResizeBuffers` alone does not make every Studio camera and post-processing pass render a valid DLSS input at the requested resolution.
- The wrapper has no robust D3D11 resource-state/synchronization and output ownership path for the live Unity swapchain.

Therefore its “native” toggle must remain experimental. Making it genuinely native requires a Unity-side input bridge plus correct NGX parameter binding, not another DLL or config switch.

### `dlss5-bridge` is not a magic native-DLSS switch

The current community bridge has two materially different modes. Mirror mode forwards a genuine DLSS contract from a DX11 title that already calls DLSS. Synthetic mode can use ReShade depth and NVIDIA optical flow to manufacture a substitute motion guide. KKS has no original DLSS call, so only the latter category applies unless the native plugin is completed. It can be a useful fallback, but depth/MV probes and image comparisons must be recorded; DLL loading is not proof of accuracy.

### Multiple add-ons silently cancel each other

Never load these together:

- ShortFuse `renodx-dlss.addon64` and DLSS5-Feeder
- `dlss5-bridge.addon64` and DLSS5-Feeder
- RenoDX DLSS5 and Deep Fried Chicken
- duplicate `.addon64` files or `.bak` files that ReShade still scans
- native `PPE_DLSS.dll` while judging the Feeder route

### HDRP/URP conversion did not solve KKS

KKS is a Unity 2019 Built-in/Forward application. HDRP/URP assets require their own shaders, renderer, resource layout, lighting data, and pipeline initialization. Assigning an editor HDRP asset to the player caused `UnityEditor`/pipeline initialization failures. HDRP can be used for an offline preview, not as a drop-in KKS runtime replacement.

## Proof checklist

Do not treat a DLL load, overlay switch, or Present hook as proof. A verified Feeder run must show:

```text
NGX feature requirements: SuperSampling ... supported
NVSDK_NGX_D3D12_Init -> 0x00000001 (Success)
feature ready: 1288x724 -> 1920x1080 DLSS Quality (synthetic jitter)
frame N delivered (..., DLSS SR, ...)
MV probe ... non-zero
Depth probe ... non-flat   # required for final temporal quality
```

The first four and motion-vector probe were verified on KKS. The final depth line requires the one-time Generic Depth resource selection.

## Rollback

1. Close CharaStudio.
2. Move the Feeder/RenoDX add-ons and NVIDIA runtime files out of the game folder.
3. Restore the timestamped backup under `D:\Koikatsu Sunshine\_codex_archive\DLSS\`.
4. Remove or disable the Feeder techniques from `ReShadePreset.ini`.
5. Keep the native plugin disabled unless testing it independently.

## Upstream references

- [DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder)
- [LumeniteFX](https://github.com/umar-afzaal/LumeniteFX)
- [RenoDX mods](https://github.com/clshortfuse/renodx/wiki/Mods)
- [ReShade Generic Depth](https://guides.martysmods.com/reshade/depth/)

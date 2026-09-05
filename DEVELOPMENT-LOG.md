# Development Log

## 2026-09-05: Native DLSS investigation and verified Feeder route

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

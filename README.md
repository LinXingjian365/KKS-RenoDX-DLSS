# KKS RenoDX DLSS

Verified DLSS integration for Koikatsu Sunshine CharaStudio (Unity 2019.4, 64-bit D3D11). The project keeps the native experiment and the working ReShade route documented separately.

## Current verified route: DLSS5-Feeder + RenoDX DLSS5

KKS has no native DLSS feature contract. The working route is:

`ReShade 6.8 add-on support -> LumeniteFX Kernel -> DLSS5-Feeder -> RenoDX DLSS5 -> NVIDIA NGX SuperSampling`

## Release status

The DLSS contract and Super Resolution path are verified. The remaining per-machine step is selecting KKS's live scene depth resource in ReShade Generic Depth. This cannot be reliably hard-coded because the resource handle is created and destroyed by the running D3D11 device; the repository ships the best automatic heuristics and documents the one-time runtime selection.

This is the route that actually produced the following runtime proof on an RTX 3060 Laptop with driver 616.56:

```text
NGX feature requirements: SuperSampling -> supported
NVSDK_NGX_D3D12_Init -> 0x00000001 (Success)
feature ready: 1288x724 -> 1920x1080 DLSS Quality (synthetic jitter)
frame N delivered (..., DLSS SR, ...)
```

`DLSSNR` is the neural-rendering feature used by the RenoDX DLSS5 consumer. It is not, by itself, the same thing as DLSS Super Resolution. The Feeder creates the missing DLSS request so KKS can reach the normal NGX SuperSampling path. See [DLSS5-FEEDER.md](DLSS5-FEEDER.md) for the exact layout and verification procedure.

The Feeder is an upstream project for this integration: [DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder). Its documented D3D11 work-resolution `work_upscale=2` path is experimental; this repository records the measured result rather than treating an add-on load as proof.

The complete native-DLSS failure ledger and troubleshooting matrix are in [DEBUG.md](DEBUG.md). The chronological engineering record is in [DEVELOPMENT-LOG.md](DEVELOPMENT-LOG.md).

The native `PPE_DLSS.dll` implementation remains explicitly experimental: its current wrapper is not shipped as a verified native-input solution. The verified working path is still DLSS5-Feeder + RenoDX DLSS5.

## Legacy/native routes

`PPE_DLSS.dll` remains an experimental native NGX path. KKS does not expose the resources and feature contract it needs reliably, so keep its toggle off when using the Feeder route. ShortFuse `renodx-dlss.addon64` is a different replacement route and must not be loaded together with DLSS5-Feeder.

## Install

Copy `PPE_DLSS.dll` to `BepInEx/plugins/` and keep the matching `nvngx_dlss.dll` beside the game executable. Press `Ctrl+D` after entering Studio. The plugin also retries while enabled if the camera was not ready when the key was pressed.

## Build

```text
dotnet build -c Release
```

Target: `net471`, references the KKS Unity 2019.4 managed assemblies.

## Known limitations

- The Feeder is a synthetic contract, but its final upscale is a genuine NGX SuperSampling evaluate; it is not a sharpen-only shader.
- A successful DLL load is not proof that NGX can create a Super Resolution feature.
- If NGX returns PlatformError or FeatureNotSupported, the switch cannot force DLSS on; the log is the source of truth.
- The route requires third-party ReShade add-ons and NVIDIA runtime files; those binary dependencies are not redistributed in this repository.
- Generic Depth must be manually pointed at KKS's scene depth draw/clear. A log line saying `Depth probe ... flat` means the wrong buffer was selected and temporal quality will be reduced even though the DLSS frames are delivered.
- No frame generation is enabled.
- Do not load ShortFuse `renodx-dlss`, `dlss5-bridge`, or a second neural consumer beside the current Feeder + RenoDX DLSS5 route.

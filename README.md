# KKS RenoDX DLSS

RenoDX DLSS + ReShade DX11 integration for Koikatsu Sunshine CharaStudio (Unity 2019.4). The repository records both the ShortFuse RenoDX DLSS route and the earlier DLSS5 bridge experiment.

## Current route: RenoDX DLSS

KKS has no native DLSS contract. The current verified route is ReShade 6.8 add-on support plus ShortFuse's `renodx-dlss.addon64`. It loads and hooks KKS's D3D11 Present path and creates a D3D12 proxy, but KKS still does not emit a native DLSS feature. Therefore this repository does not claim that traditional DLSS Super Resolution is active in KKS.

The exact setup and current limitation are documented in [RESHade-DLSS.md](RESHade-DLSS.md). Runtime proof for the integration is `ReShade.log` showing `Registered add-on "RenoDX DLSS"`, `init_device`, `init_swapchain`, and `first present`. A DLSS feature create/evaluate line is required before claiming actual upscaling.

## Legacy native route

`PPE_DLSS.dll` remains an experimental native NGX path. KKS does not expose the resources and feature contract it needs reliably, so keep its toggle off when using RenoDX. A DLL being present or a shortcut being accepted is not proof that native DLSS is active.

## Install

Copy `PPE_DLSS.dll` to `BepInEx/plugins/` and keep the matching `nvngx_dlss.dll` beside the game executable. Press `Ctrl+D` after entering Studio. The plugin also retries while enabled if the camera was not ready when the key was pressed.

## Build

```text
dotnet build -c Release
```

Target: `net471`, references the KKS Unity 2019.4 managed assemblies.

## Known limitations

- This is native NGX, not a post-process shader approximation.
- A successful DLL load is not proof that NGX can create a Super Resolution feature.
- If NGX returns PlatformError or FeatureNotSupported, the switch cannot force DLSS on; the log is the source of truth.
- The RenoDX route requires third-party ReShade add-ons and Streamline/NVIDIA runtime files; those binary dependencies are not redistributed in this repository.
- No frame generation is enabled.
- The native route and RenoDX route should not be enabled as competing upscalers in the same session.

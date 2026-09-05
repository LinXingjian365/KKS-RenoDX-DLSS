# KKS DLSS / RenoDX integration notes

Koikatsu Sunshine CharaStudio (Unity 2019.4, D3D11) integration notes and the legacy native NGX experiment.

## Recommended route: RenoDX DLSS 5 bridge

KKS has no native DLSS contract. The verified route is ReShade 6.8 add-on support plus RenoDX DLSS 5 and the DX11 bridge. It uses the substitute contract from ReShade depth and NVIDIA Optical Flow, so it is not equivalent to native game DLSS and may soften text or moving fine detail.

The exact verified setup is documented in [RESHade-DLSS.md](RESHade-DLSS.md). The important runtime proof is `dlss5-bridge.log` showing `session ready`, `feature ready`, `motion vectors (NVIDIA optical flow): bound`, and `frames delivered`.

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

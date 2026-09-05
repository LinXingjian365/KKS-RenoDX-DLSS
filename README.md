# KKS DLSS Upscaler

Native NGX integration experiment for Koikatsu Sunshine CharaStudio (Unity 2019.4, D3D11).

## Important status

This project does not use ReShade. KKS is a Unity 2019 built-in/D3D11 application, and native DLSS requires the real Unity D3D11 device, valid depth and motion-vector resources, and a driver-supported NGX feature. The plugin now retries after Studio cameras appear and reports the exact initialization state, but it must not claim DLSS is active until the log says `Initialization successful!` and the overlay says `DLSS ON`.

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
- No ReShade, Streamline proxy, frame-generation DLL, or NIS fallback is included.

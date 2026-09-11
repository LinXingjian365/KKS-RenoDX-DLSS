# KKS RenoDX DLSS

Stable native DLSS Super Resolution integration for Koikatsu Sunshine CharaStudio (v3.0.0). The plugin captures Unity's render-sized color, depth, and motion-vector textures, submits them through an official NVIDIA NGX D3D12 bridge, and presents the synchronized output back through Unity's D3D11 image effect.

## v3.0.0 release status

The native path has been verified in Studio on an RTX 3060 Laptop GPU with NVIDIA driver 616.56:

```text
NGX input=960x540 output=1920x1080 mode=MaxQuality
MVScale=1.0x1.0
D3D12 Evaluate=success
D3D12 output copy-back=success
DLSS output validation: maxChannel=0.90588, usable=True
evalFrames=602, privateCopies=602, privateCopyFailures=0
```

The measured steady-state bridge wait is about 7–10 ms on that machine. The first frame can take longer while NGX allocates its history. The plugin keeps the original source visible until the native output passes a non-zero validation check.

## Runtime path

```text
KKS D3D11 camera
  -> Unity color/depth/motion capture
  -> D3D11 shared relays with keyed-mutex ownership
  -> official NVIDIA SDK D3D12/NGX Super Resolution
  -> fenced output relay
  -> Unity D3D11 output texture
```

Unity's motion-vector texture is normalized UV displacement, so the bridge uses `MVScale=1x1`. The current quality preset is MaxQuality at 960x540 input and 1920x1080 output. Frame generation is not enabled.

## Install

Copy these files to `D:\Koikatsu Sunshine\BepInEx\plugins`:

- `PPE_DLSS.dll`
- `kks_dlss_d3d12_bridge.dll`

Keep the matching NVIDIA NGX runtime supplied by the game beside the executable. Enter Studio and press `Ctrl+D` to toggle DLSS. The default is off, and the plugin waits for the final Studio camera before attaching.

Do not load the native plugin together with another DLSS consumer such as ShortFuse `renodx-dlss.addon64`, `dlss5-bridge.addon64`, or a second neural-rendering replacement.

## Build and verification

```text
dotnet build PPE_DLSS.csproj --configuration Release
powershell -ExecutionPolicy Bypass -File native/build_probe.ps1
powershell -ExecutionPolicy Bypass -File tools/verify_dlss_log.ps1 -LogPath <charaStudio-log.txt>
```

The verifier accepts either the v3 native bridge markers or the archived Feeder route markers. For a native release run, require `MVScale=1x1`, successful staging, at least three successful evaluations, successful copy-back, and `usable=True` output validation.

## Alternate Feeder route

The older ReShade + LumeniteFX + DLSS5-Feeder + RenoDX route remains documented in [DLSS5-FEEDER.md](DLSS5-FEEDER.md) for comparison and rollback. It is a separate route and must not be active while validating the native plugin.

## Known limits

- The native path is validated on Unity 2019.4 CharaStudio with D3D11 and the tested NVIDIA driver/runtime combination. Other drivers may require a new runtime check.
- Depth and motion probes can be near zero in a static scene; moving the camera is required when checking temporal input.
- No frame generation is included.
- NVIDIA runtime binaries and third-party add-ons are not redistributed here.
- Historical experiments and failure analysis remain in [DEBUG.md](DEBUG.md) and [DEVELOPMENT-LOG.md](DEVELOPMENT-LOG.md).

# RenoDX DLSS on KKS

## Verified environment

- Koikatsu Sunshine CharaStudio, Unity 2019.4.9, D3D11
- ReShade 6.8.0.2155 with add-on support
- ShortFuse `renodx-dlss.addon64` SF `0.52`
- NVIDIA driver `32.0.16.1656`
- `nvngx_dlss.dll` and `nvngx_dlssnr.dll` `310.8.0.0`

## What this route can and cannot do

KKS is a Unity 2019 D3D11 game with no native DLSS feature creation. The ShortFuse add-on loads and hooks the D3D11 Present path and creates a D3D12 proxy, which is the correct RenoDX integration point. That is not the same as a working DLSS Super Resolution pass: a real `CreateFeature`/`EvaluateFeature` pair is still required.

The old generic `renodx-dlss5` plus `dlss5-bridge` stack is archived and must not be loaded beside ShortFuse's add-on. If a synthetic no-DLSS contract is desired, use a dedicated Feeder route; it is a different pipeline and must not be mixed with this add-on.

## Required layout

Place these beside `CharaStudio.exe`:

- `dxgi.dll` from ReShade add-on support
- `ReShade.ini`
- `renodx-dlss.addon64`
- `nvngx_dlss.dll` version 3.1.13 or newer
- `nvngx_dlssnr.dll`
- matching Streamline files, including `sl.interposer.dll`

Do not leave old `renodx-dlss5.addon64.bak` files in an active add-on directory. ReShade may load them and the bridge reports multiple DLSS add-ons.

## Required ReShade.ini values

```ini
[ADDON]
AddonPath=D:\Koikatsu Sunshine\
LoadFromDllMain=renodx-dlss.addon64
DisabledAddons=Effect Runtime Sync

[RENODX-DLSS]
OptionsMode=2
DLSSQualityMode=2
DirectNeuralRenderingHookPoint=5
DirectNeuralRenderingForceNgxCore=0
DirectNeuralRenderingRequireDlss=1
```

Those three `RENODX-DLSS` values are for KKS's DX11 path without a native DLSS contract: hook on Present, force the NGX core path, and do not require the game to expose DLSS.

Enable `Generic Depth` by removing it from `DisabledAddons`. KKS needs it for the substitute contract.

Do not leave `dlss5-bridge.addon64`, `dlss5-bridge.cfg`, or `nvngx_dlssnr.dll` active when testing the SR/DLAA route.

## Runtime proof

Do not judge success from the Home overlay switch alone. `ReShade.log` must contain:

- `Registered add-on "RenoDX DLSS"`
- `init_device`
- `init_swapchain`
- `first present`
- a real DLSS `CreateFeature` and `EvaluateFeature` pair

The current KKS test reaches the first three integration markers. It does not yet produce a native DLSS feature because KKS never creates one.

### `feature 18 create failed with 0xBAD00002`

Check the runtime before changing KKS settings:

```powershell
Get-AuthenticodeSignature .\nvngx_dlssnr.dll
Get-FileHash .\nvngx_dlssnr.dll -Algorithm SHA256
```

The community `streamline.zip` package is known to have shipped a `HashMismatch` copy of the 310.8.0.0 NR runtime. Do not patch or re-sign it locally. Obtain the verified NVIDIA-signed copy from the RenoDX pinned download channel, back up the current file, replace only `nvngx_dlssnr.dll`, and verify it again. NVIDIA runtime files are not redistributed by this repository.

After replacement, restart Studio and force a contract recreation by changing the upscaler/quality setting once. The `0xBAD00002` failure is a runtime validation failure, not a KKS scene or depth-buffer failure. For the current SR/DLAA test, keep the NR runtime disabled.

The verified KKS run delivered 1200 frames, reported `brightness out/in ... 0.99`, and reached approximately 82.7 FPS with roughly 5% bridge frame cost.

## Rollback

Close Studio and remove only the RenoDX/bridge add-ons and Streamline files, then restore the timestamped backup under `D:\Koikatsu Sunshine\_codex_archive\DLSS\`. Keep the ReShade files separate from the native `PPE_DLSS.dll` experiment.

# RenoDX DLSS 5 on KKS

## Verified environment

- Koikatsu Sunshine CharaStudio, Unity 2019.4.9, D3D11
- ReShade 6.8.0.2155 with add-on support
- `renodx-dlss5` build `0.2026.827.2036`
- `dlss5-bridge` `1.4.8`
- NVIDIA driver `32.0.16.1656`
- `nvngx_dlss.dll` and `nvngx_dlssnr.dll` `310.8.0.0`

## Why the old switch did nothing

The normal RenoDX DLSS add-on waits for a game's own DLSS call. KKS has no such call. The DX11 bridge is required to create the substitute contract. The previous log proved the failure: `Streamline interposer not found`, `synth=0`, flat depth and unbound motion vectors.

The bridge and Neural Rendering are separate checks. `feature ready`, `every precondition holds`, and `frames delivered` prove that the synthetic contract is being fed, but they do not prove that RenoDX's Feature 18 inference succeeded. The RenoDX status panel must show `DLSSNR ACTIVE` and a rising successful-NR-frame counter.

## Required layout

Place these beside `CharaStudio.exe`:

- `dxgi.dll` from ReShade add-on support
- `ReShade.ini`
- `renodx-dlss5.addon64`
- `dlss5-bridge.addon64`
- `nvngx_dlss.dll` version 3.1.13 or newer
- `nvngx_dlssnr.dll`
- matching Streamline files, including `sl.interposer.dll`

Do not leave old `renodx-dlss5.addon64.bak` files in an active add-on directory. ReShade may load them and the bridge reports multiple DLSS add-ons.

## Required ReShade.ini values

```ini
[ADDON]
AddonPath=D:\Koikatsu Sunshine\
LoadFromDllMain=renodx-dlss5.addon64,dlss5-bridge.addon64
DisabledAddons=Effect Runtime Sync

[RenoDX.DLSS5]
DLSSNR.Enabled=1
NeuralUplift=1
NREnableUpscaling=1
NRToggleKey=117

[RENODX-DLSS]
DirectNeuralRenderingHookPoint=5
DirectNeuralRenderingForceNgxCore=1
DirectNeuralRenderingRequireDlss=0
```

Those three `RENODX-DLSS` values are for KKS's DX11 path without a native DLSS contract: hook on Present, force the NGX core path, and do not require the game to expose DLSS.

Enable `Generic Depth` by removing it from `DisabledAddons`. KKS needs it for the substitute contract.

In `dlss5-bridge.cfg`, use `synth=1`, `synth_after=10`, `source=auto`, `ofa_grid=2`, `stage=3`, `mode=2`, and `unwrap=1`.

## Runtime proof

Do not judge success from the Home overlay switch alone. `dlss5-bridge.log` must contain:

- `DLSS 5 add-on settings ... DLSSNR.Enabled=1`
- `session ready`
- `feature ready`
- `motion vectors (NVIDIA optical flow): bound`
- `feature ... presentation ... contract SDR/HDR`
- repeated `frames delivered`

For actual Neural Rendering, also require a signed-runtime initialization line such as `signed DLSSNR ... initialized`, no `0xBAD00002`, and `DLSSNR ACTIVE` in the RenoDX status panel.

### `feature 18 create failed with 0xBAD00002`

Check the runtime before changing KKS settings:

```powershell
Get-AuthenticodeSignature .\nvngx_dlssnr.dll
Get-FileHash .\nvngx_dlssnr.dll -Algorithm SHA256
```

The community `streamline.zip` package is known to have shipped a `HashMismatch` copy of the 310.8.0.0 NR runtime. Do not patch or re-sign it locally. Obtain the verified NVIDIA-signed copy from the RenoDX pinned download channel, back up the current file, replace only `nvngx_dlssnr.dll`, and verify it again. NVIDIA runtime files are not redistributed by this repository.

After replacement, restart Studio and force a contract recreation by changing the upscaler/quality setting once. The `0xBAD00002` failure is a runtime validation failure, not a KKS scene or depth-buffer failure.

The verified KKS run delivered 1200 frames, reported `brightness out/in ... 0.99`, and reached approximately 82.7 FPS with roughly 5% bridge frame cost.

## Rollback

Close Studio and remove only the RenoDX/bridge add-ons and Streamline files, then restore the timestamped backup under `D:\Koikatsu Sunshine\_codex_archive\DLSS\`. Keep the ReShade files separate from the native `PPE_DLSS.dll` experiment.

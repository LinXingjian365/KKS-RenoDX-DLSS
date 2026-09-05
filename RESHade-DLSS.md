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
```

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

The verified KKS run delivered 1200 frames, reported `brightness out/in ... 0.99`, and reached approximately 82.7 FPS with roughly 5% bridge frame cost.

## Rollback

Close Studio and remove only the RenoDX/bridge add-ons and Streamline files, then restore the timestamped backup under `D:\Koikatsu Sunshine\_codex_archive\DLSS\`. Keep the ReShade files separate from the native `PPE_DLSS.dll` experiment.

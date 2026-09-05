# KKS DLSS5-Feeder setup

This is the reproducible setup that was verified on Koikatsu Sunshine CharaStudio:

- Unity 2019.4.9f1, 64-bit D3D11
- ReShade 6.8 with add-on support
- `dlss5-feed.addon64` 0.14.0-beta.1
- `DLSS5_Feed.fx`
- LumeniteFX mainline `Lumenite_Kernel`
- RenoDX `renodx-dlss5.addon64` v4.7
- `nvngx_dlss.dll` 310.8.0.0 and `nvngx_dlssnr.dll` 310.8.1.0
- NVIDIA driver 616.56

## File layout

Place the add-ons and NVIDIA runtime files next to `CharaStudio.exe`:

```text
D:\Koikatsu Sunshine\
  dlss5-feed.addon64
  renodx-dlss5.addon64
  nvngx.dll
  _nvngx.dll
  nvngx_dlss.dll
  nvngx_dlssnr.dll
  ReShade.ini
  ReShadePreset.ini
  dlss5-feed.cfg
  reshade-shaders\Shaders\DLSS5_Feed.fx
  reshade-shaders\Shaders\lumenite_Kernel.fx
  reshade-shaders\Shaders\lumenite_QuantMotion.fx
  reshade-shaders\Shaders\include\lumenite_*.fxh
```

The working `nvngx.dll` and `_nvngx.dll` were copied from the installed NVIDIA driver, not from the old Streamline package. The package copy returned NGX `0xBAD00002`; the driver copy initialized successfully.

## Known-good settings

`ReShade.ini`:

```ini
[GENERAL]
PreprocessorDefinitions=DLSS5_MV_PROVIDER=3,RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000.0,RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=1,RESHADE_DEPTH_INPUT_IS_REVERSED=1,RESHADE_DEPTH_INPUT_IS_LOGARITHMIC=0

[DEPTH]
DepthCopyAtClearIndex=1
DepthCopyBeforeClears=2
DrawStatsHeuristic=0
FilterFormat=0
UseAspectRatioHeuristics=3
```

`ReShadePreset.ini` must enable the effects in this order:

```ini
Techniques=Lumenite_Kernel,DLSS5_Feed
```

`dlss5-feed.cfg` for the current Quality test:

```ini
enabled=1
mode=2
work_resolution=67
work_upscale=2
work_sharpness=0.30
```

`work_upscale=2` is Feeder's experimental synthetic-jitter DLSS Quality path. It is not the same as merely setting `work_resolution` below 100: `work_upscale=0` only stretches the result, while `work_upscale=2` produces the runtime proof `DLSS SR`.

## How to verify

Do not use a successful DLL load as proof. Check `dlss5-feed.log` for all of these:

```text
NGX feature requirements: SuperSampling ... supported
NVSDK_NGX_D3D12_Init -> 0x00000001 (Success)
feature ready: ... -> ... DLSS Quality (synthetic jitter)
frame N delivered (... DLSS SR ...)
MV probe ... non-zero
Depth probe ... non-flat
```

The current KKS run passes the first four and motion-vector probe. Its remaining issue is a flat depth probe, which means ReShade Generic Depth is selecting a cleared/UI buffer. Open ReShade's **Add-ons -> Generic Depth**, use its preview/statistics, and select the draw call or clear that contains the 3D scene. The correct result is a non-flat depth image; changing DLSS or RenoDX settings cannot repair a wrong depth source.

## Do not combine these routes

- Do not load ShortFuse `renodx-dlss.addon64` with the Feeder.
- Do not load `dlss5-bridge.addon64` with the Feeder; the Feeder already creates the missing request.
- Do not load two neural consumers. Keep only RenoDX DLSS5 or switch completely to Deep Fried Chicken.
- Keep the native `PPE_DLSS.dll` toggle off while testing this route.

## Upstream references

- [DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder)
- [LumeniteFX](https://github.com/umar-afzaal/LumeniteFX)
- [RenoDX mods](https://github.com/clshortfuse/renodx/wiki/Mods)

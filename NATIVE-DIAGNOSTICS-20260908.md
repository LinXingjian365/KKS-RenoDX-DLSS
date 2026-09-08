# Native NGX investigation: 2026-09-08

## Reproduction

Run `native/bridge_probe.exe "D:\Koikatsu Sunshine"` after building with
`native/build_probe.ps1`. The probe now tests feature creation, not just session
initialization. Non-successful creation returns a nonzero exit status.

Observed on this machine:

```text
init result=0x00000001
isolated CreateFeature=0xBAD0000B
SR available=0 (query=0x00000001)
needsDriver=0 (query=0x00000001)
initResult=0xBAD00004 (query=0x00000001)
```

All capability queries succeeded. NGX reports FeatureNotFound for SuperSampling.
The same result occurs with the installed nvngx_dlss.dll beside the probe.
This reproduces without Unity, live relays, or game plugins. It does not prove
which loader/search-path/runtime condition causes FeatureNotFound. Driver update
is not requested by this capability report.

## Corrections to earlier conclusions

- CreateFeature failure alone does not prove invalid texture formats or flags.
- The NVIDIA creation helper sets dimensions, quality, flags, and node masks;
  color/depth/motion resource bindings belong to evaluation. Delaying creation
  until live input arrival did not solve the underlying failure.
- Stage code 8 proves the staging code reached its success branch. It does not
  validate GPU completion, synchronization, pixels, or motion-vector accuracy.
- Disabling is logged by the shortcut toggle path. The supplied log does not
  establish an automatic lifecycle failure.
- Input capture must not display DLSS ON or reduce resolution without output.

## Changes and limits

Direct DLL initialization now uses the four-argument snippet ABI documented in
the local NVIDIA header rather than the five-argument SDK wrapper signature.
GetCapabilityParameters exposes the internal failure report. The isolated probe
includes a feature-creation test; session initialization alone no longer passes.
Both native and managed builds pass. Feature creation still fails. Evaluation,
GPU fences, output copy-back, and accurate temporal inputs are not validated.

Next investigation: resolve how the loaded NGX core discovers and initializes
the SuperSampling runtime, using loader evidence and a supported SDK init path.

Reference: https://github.com/NVIDIA/DLSS/blob/main/include/nvsdk_ngx_helpers.h

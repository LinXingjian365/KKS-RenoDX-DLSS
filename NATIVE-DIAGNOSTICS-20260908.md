# Native NGX investigation: 2026-09-08

> **Resolved in v3.0.0 (2026-09-11):** the former feature/output failures in
> this record were cleared by the official SDK loader path, live relay
> resources, normalized `MVScale=1x1`, and keyed-mutex D3D11/D3D12 handoff.
> The in-game acceptance run now reports non-zero output and 602 successful
> evaluations. The sections below preserve the original bring-up evidence.

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

## Resolved: SDK loader versus direct core exports

The new `sdk_probe.cpp` links NVIDIA's supplied `nvsdk_ngx_d.lib`, calls the
SDK's Init_with_ProjectID with an explicit runtime search path, then queries
capabilities and creates SuperSampling. With exactly the existing game-root
runtime it reports:

```text
SDK init=0x00000001
capabilities=0x00000001 available=1 featureInit=0x00000001
SDK CreateFeature=0x00000001
```

The production bridge now uses the SDK for init, capability queries, creation,
parameter destruction, feature release and shutdown. Its independent probe
also returns success, including command submission and fence completion for
creation. Removing direct core export calls and providing the runtime search
path together resolves the observed FeatureNotFound; this test does not isolate
which internal SDK loader action was missing.

No driver or game runtime DLL replacement was necessary. The discovered old
plugin-directory runtime has not been deleted or declared the proven cause.
The standalone SDK probe tests creation only; the bridge probe additionally
submits creation commands and waits. Neither proves frame evaluation or output
copy-back. KKS pixel, synchronization and temporal quality tests remain pending.

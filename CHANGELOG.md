# Changelog

## v2.5.1 - 2026-09-08

- Deferred native D3D12 DLSS feature creation until live KKS color, depth, and motion-vector relay textures are staged.
- Recorded the verified in-game adapter attach and three-resource staging result.
- Prevented the old dummy-resource `0xBAD0000B` result from being reported as the live feature result.

## v2.4.3

- Replaced the minimal debug note with the complete native-DLSS failure ledger.
- Added `DEVELOPMENT-LOG.md` covering the native plugin, ShortFuse, bridge, DLSSNR, HDRP/URP, runtime, driver, depth, performance, and add-on-conflict lessons.
- Added proof criteria that distinguish a loaded DLL from a real NGX Super Resolution frame.

## v2.4.4

- Recorded the MSAA-off isolation test: DLSS SR remained active while depth stayed flat.
- Audited the native `PPE_DLSS` source and documented why its zeroed motion vectors and empty depth texture are not valid native inputs.
- Added the concrete native-input bridge milestones instead of presenting the proof-of-concept wrapper as complete.

## v2.4.5

- Added the first native-input experiment: bind Unity `_CameraDepthTexture` and `_CameraMotionVectorsTexture` resources to NGX at evaluate time.
- Built and deployed the experiment with the native toggle left off; the Feeder route remains the active verified route.
- Kept the native output/synchronization path explicitly experimental until live KKS captures validate it.

## v2.4.6

- Added official D3D11 typed-resource binding and `EvaluateFeature_C` usage to the native experiment.
- Added live Unity depth/motion-vector resource capture and a custom ProjectID initialization attempt.
- Recorded the isolated result: KKS exposes a valid D3D11 device, but the current NGX runtime returns `PlatformError` and lacks the ProjectID export.
- Defined the next native milestone as a private D3D12 bridge; restored RenoDX/Feeder as the active fallback after testing.

## v2.4.2

- Marked the Feeder route as theoretically complete and runtime-verified for DLSS Super Resolution.
- Clarified that only the live Generic Depth resource selection remains machine/runtime-specific.
- Kept the automatic depth heuristics as the safe default and documented why the resource handle is not hard-coded.

## v2.4.0

- Switched the documented and tested route to DLSS5-Feeder plus LumeniteFX Kernel and RenoDX DLSS5.
- Verified genuine NGX SuperSampling on KKS: `NVSDK_NGX_D3D12_Init` succeeds and `DLSS SR` frames are delivered at 67% work resolution to a 1920x1080 backbuffer.
- Added `DLSS5-FEEDER.md` with the exact file layout, settings, verification markers, and conflict rules.
- Documented the distinction between DLSSNR neural rendering and DLSS Super Resolution.
- Documented the remaining Generic Depth selection requirement instead of treating flat depth as a successful quality result.
- Added the measured verification record in `VERIFICATION-20260905.md`.

## Repository rename

The project is now published as **KKS-RenoDX-DLSS** so the name matches the active implementation route. The former DLSS5 bridge experiment remains archived and is not loaded by the current KKS profile.

## v2.2.1

- Added the RenoDX DX11/no-native-DLSS values `HookPoint=5`, `ForceNgxCore=1`, and `RequireDlss=0`.
- Documented the difference between a functioning DLSS 5 Bridge contract and actual Neural Rendering activation.
- Documented the known `nvngx_dlssnr.dll` `HashMismatch` failure and signed-runtime verification procedure.

## v2.2.0

- Documented and verified the RenoDX DLSS 5 + DX11 bridge route for KKS.
- Added the known-good file layout, configuration, runtime proof markers and rollback procedure.
- Clarified that the native NGX plugin is experimental and should remain off when RenoDX is active.

## v2.1.0

- Retry initialization when Studio creates its camera after the shortcut is pressed.
- Report graphics API, shader model, compute support, and driver string.
- Show explicit `WAITING_FOR_CAMERA`, `INIT_FAILED`, and `UNSUPPORTED_API` states.
- Request camera depth and motion-vector textures before native initialization.
## 2.5.0 - 2026-09-07

- Added `tools/verify_dlss_log.ps1` with separate transport and strict temporal-input quality gates.
- Documented the current measured state: NGX evaluation and frame delivery pass, but KKS depth/MV probes are flat and do not pass the accuracy gate.
- Documented the actual scope of `dlss5-bridge` mirror versus synthetic modes.
- Excluded the local DLSSTweaks research clone from source control.
- Stopped native-plugin retry storms after a terminal NGX D3D11 `PlatformError`/missing ProjectID export.
- Validated the private D3D12 NGX standard-init path against KKS's exact `_nvngx.dll`; isolated probe returns success with AppID `0`.
- Added isolated D3D12 queue/list, parameter, resource, and `CreateFeature` probe; records the current `UnableToInitializeFeature` boundary.
- Added adapter-LUID checked D3D11-to-D3D12 shared-texture staging primitives for the native bridge.

# Changelog

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

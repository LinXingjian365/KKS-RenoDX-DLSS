# Changelog

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

# Debugging

1. Confirm `KKS DLSS Upscaler v2.1.0 loaded`.
2. Confirm `Graphics=Direct3D11`.
3. Press `Ctrl+D` after Studio is fully inside a scene.
4. Look for `DLSS init:` and the final NGX result.
5. Only `Initialization successful!` plus `DLSS ON` means the feature is active.

The older implementation latched failure if no camera existed at the first key press. That behavior is fixed. Native NGX `PlatformError` or `FeatureNotSupported` remains an external compatibility failure, not a UI-toggle failure.

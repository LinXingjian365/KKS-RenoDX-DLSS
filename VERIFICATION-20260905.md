# Verification 2026-09-05

Host: Koikatsu Sunshine CharaStudio, Unity 2019.4.9f1, 64-bit D3D11, RTX 3060 Laptop GPU, NVIDIA driver 616.56.

## Passed

```text
NGX feature requirements: SuperSampling -> supported
NVSDK_NGX_D3D12_Init -> 0x00000001 (Success)
building: 1288x724 work resolution (67%) -> 1920x1080 backbuffer
DLSS Quality at 1920x1080: optimal 1280x720
work_upscale=2: DLSS Quality, 1288x724 -> 1920x1080
feature ready: 1288x724 -> 1920x1080 DLSS Quality (synthetic jitter)
frame 1 delivered (..., DLSS SR, ...)
MV probe ... 74% non-zero
```

This proves the KKS D3D11 frame is reaching NVIDIA NGX SuperSampling and that the output is being returned at 1920x1080. It is not just a DLL load, a DLSSNR-only pass, or a sharpen shader.

## Remaining quality issue

```text
Depth probe ... min 0, max 0, mean 0, variance 0, 100% finite
```

Generic Depth is still bound to a cleared/UI buffer. The neural pass is active, but temporal reconstruction has no scene depth until the correct KKS scene depth draw/clear is selected in ReShade's Generic Depth add-on. This is an explicit quality limitation, not a failure of the DLSS contract.

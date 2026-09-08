$ErrorActionPreference = 'Stop'
$vs = 'C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat'
$source = Join-Path $PSScriptRoot 'sdk_probe.cpp'
$output = Join-Path $PSScriptRoot 'sdk_probe.exe'
$library = Join-Path $PSScriptRoot 'third_party\DLSS\lib\Windows_x86_64\x64\nvsdk_ngx_d.lib'
cmd /c "call `"$vs`" && cl /nologo /MD /EHsc /std:c++17 `"$source`" /Fe:`"$output`" `"$library`" d3d12.lib dxgi.lib advapi32.lib user32.lib"
if ($LASTEXITCODE -ne 0) { throw "SDK probe build failed: $LASTEXITCODE" }

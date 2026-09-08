$vs = 'C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat'
$src = Join-Path $PSScriptRoot 'ngx_d3d12_probe.cpp'
$out = Join-Path $PSScriptRoot 'ngx_d3d12_probe.exe'
cmd /c "call `"$vs`" && cl /nologo /EHsc /std:c++17 /I`"$PSScriptRoot\third_party\DLSS\include`" `"$src`" /Fe:`"$out`" d3d12.lib dxgi.lib"

$bridgeSrc = Join-Path $PSScriptRoot 'kks_dlss_d3d12_bridge.cpp'
$bridgeOut = Join-Path $PSScriptRoot 'kks_dlss_d3d12_bridge.dll'
$sdkLib = Join-Path $PSScriptRoot 'third_party\DLSS\lib\Windows_x86_64\x64\nvsdk_ngx_d.lib'
cmd /c "call `"$vs`" && cl /nologo /MD /LD /EHsc /std:c++17 /I`"$PSScriptRoot\third_party\DLSS\include`" `"$bridgeSrc`" /Fe:`"$bridgeOut`" `"$sdkLib`" d3d12.lib dxgi.lib advapi32.lib user32.lib"
if ($LASTEXITCODE -ne 0) { throw 'Native bridge build failed' }

$bridgeProbeSrc = Join-Path $PSScriptRoot 'bridge_probe.cpp'
$bridgeProbeOut = Join-Path $PSScriptRoot 'bridge_probe.exe'
cmd /c "call `"$vs`" && cl /nologo /EHsc /std:c++17 `"$bridgeProbeSrc`" /Fe:`"$bridgeProbeOut`""

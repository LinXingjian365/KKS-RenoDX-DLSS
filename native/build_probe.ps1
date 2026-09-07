$vs = 'C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat'
$src = Join-Path $PSScriptRoot 'ngx_d3d12_probe.cpp'
$out = Join-Path $PSScriptRoot 'ngx_d3d12_probe.exe'
cmd /c "call `"$vs`" && cl /nologo /EHsc /std:c++17 /I`"$PSScriptRoot\third_party\DLSS\include`" `"$src`" /Fe:`"$out`" d3d12.lib dxgi.lib"

$bridgeSrc = Join-Path $PSScriptRoot 'kks_dlss_d3d12_bridge.cpp'
$bridgeOut = Join-Path $PSScriptRoot 'kks_dlss_d3d12_bridge.dll'
cmd /c "call `"$vs`" && cl /nologo /LD /EHsc /std:c++17 /I`"$PSScriptRoot\third_party\DLSS\include`" `"$bridgeSrc`" /Fe:`"$bridgeOut`" d3d12.lib dxgi.lib"

$bridgeProbeSrc = Join-Path $PSScriptRoot 'bridge_probe.cpp'
$bridgeProbeOut = Join-Path $PSScriptRoot 'bridge_probe.exe'
cmd /c "call `"$vs`" && cl /nologo /EHsc /std:c++17 `"$bridgeProbeSrc`" /Fe:`"$bridgeProbeOut`""

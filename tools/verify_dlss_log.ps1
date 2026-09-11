param(
    [Parameter(Mandatory = $false)]
    [string]$LogPath = 'D:\Koikatsu Sunshine\dlss5-feed.log'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
    Write-Error "DLSS log not found: $LogPath"
}

$text = Get-Content -LiteralPath $LogPath -Raw
$checks = [ordered]@{}

# v3.0 native bridge evidence. Keep the older Feeder checks below so this
# verifier remains useful for archived/alternate-route logs.
$nativeMode = $text -match '(?im)NGX input=\d+x\d+ output=\d+x\d+ mode=MaxQuality'
$nativeEvalCount = ([regex]::Matches($text, '(?im)D3D12 Evaluate=success')).Count
$nativeOutputUsable = $text -match '(?im)DLSS output validation:\s*maxChannel=([0-9.]+),\s*usable=True'
$nativeCopyBack = $text -match '(?im)D3D12 output copy-back=success'
$nativeMvScale = $text -match '(?im)MVScale=1(?:\.0)?x1(?:\.0)?'
$nativeStaging = $text -match '(?im)staging codes:\s*color=8\(0x00000000\),\s*depth=8\(0x00000000\),\s*motion=8\(0x00000000\),\s*output=8\(0x00000000\)'
$nativeMotion = $false
foreach ($m in [regex]::Matches($text, '(?im)motionMax=([0-9.]+)')) {
    if ([double]$m.Groups[1].Value -gt 0.001) { $nativeMotion = $true; break }
}

if ($nativeMode) {
    $checks['native MV scale 1x1'] = $nativeMvScale
    $checks['native shared staging'] = $nativeStaging
    $checks['native Evaluate success'] = $nativeEvalCount -ge 3
    $checks['native output copy-back'] = $nativeCopyBack
    $checks['native non-zero output'] = $nativeOutputUsable
    $checks['native motion observed'] = $nativeMotion
    $nativeOk = $nativeMvScale -and $nativeStaging -and ($nativeEvalCount -ge 3) -and $nativeCopyBack -and $nativeOutputUsable
    Write-Host "DLSS log: $LogPath"
    foreach ($entry in $checks.GetEnumerator()) {
        $state = if ($entry.Value) { 'PASS' } else { 'FAIL' }
        Write-Host ("[{0}] {1}" -f $state, $entry.Key)
    }
    Write-Host ""
    if ($nativeOk) {
        Write-Host '[PASS] Native D3D12 NGX bridge produced usable output.'
        exit 0
    }
    Write-Host '[FAIL] Native D3D12 NGX output is not fully proven.'
    exit 2
}

$checks['session ready'] = $text -match '(?im)session ready:'
$checks['feature ready'] = $text -match '(?im)feature ready:'
$frameCount = ([regex]::Matches($text, '(?im)frame\s+\d+\s+delivered')).Count
$checks['three delivered frames'] = $frameCount -ge 3

$mvMatch = [regex]::Match($text, '(?im)MV probe.*?mean \|mv\|\s+([0-9.]+)\s+px,\s+max\s+([0-9.]+)\s+px,\s+([0-9.]+)%\s+non-zero')
$depthMatch = [regex]::Match($text, '(?im)Depth probe.*?min\s+([-0-9.]+),\s+max\s+([-0-9.]+),\s+mean\s+([-0-9.]+),\s+variance\s+([-0-9.]+),\s+([0-9.]+)%\s+finite')

$mvPresent = $mvMatch.Success
$depthPresent = $depthMatch.Success
$checks['motion probe present'] = $mvPresent
$checks['depth probe present'] = $depthPresent

$mvUsable = $false
if ($mvPresent) {
    $mvMean = [double]$mvMatch.Groups[1].Value
    $mvMax = [double]$mvMatch.Groups[2].Value
    $mvNonZero = [double]$mvMatch.Groups[3].Value
    $mvUsable = ($mvMean -gt 0.001) -or ($mvMax -gt 0.05) -or ($mvNonZero -gt 1.0)
}
$checks['usable motion vectors'] = $mvUsable

$depthUsable = $false
if ($depthPresent) {
    $depthMin = [double]$depthMatch.Groups[1].Value
    $depthMax = [double]$depthMatch.Groups[2].Value
    $depthMean = [double]$depthMatch.Groups[3].Value
    $depthVariance = [double]$depthMatch.Groups[4].Value
    $depthFinite = [double]$depthMatch.Groups[5].Value
    $depthUsable = ($depthFinite -gt 1.0) -and (($depthMax - $depthMin) -gt 0.0001) -and ($depthVariance -gt 0.000001)
}
$checks['non-flat scene depth'] = $depthUsable

Write-Host "DLSS log: $LogPath"
Write-Host "Delivered frames: $frameCount"
foreach ($entry in $checks.GetEnumerator()) {
    $state = if ($entry.Value) { 'PASS' } else { 'FAIL' }
    Write-Host ("[{0}] {1}" -f $state, $entry.Key)
}

$transportOk = $checks['session ready'] -and $checks['feature ready'] -and $checks['three delivered frames']
$strictOk = $transportOk -and $checks['usable motion vectors'] -and $checks['non-flat scene depth']

Write-Host ""
if ($transportOk) {
    Write-Host '[PASS] NGX transport/evaluation is active.'
} else {
    Write-Host '[FAIL] NGX transport/evaluation is not proven.'
}
if ($strictOk) {
    Write-Host '[PASS] Strict temporal-input quality gate passed.'
} else {
    Write-Host '[FAIL] Strict temporal-input quality gate failed; do not claim accurate native DLSS.'
}

if (-not $strictOk) { exit 2 }

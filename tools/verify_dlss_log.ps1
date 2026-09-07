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

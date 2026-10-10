param([int]$WarmupSeconds = 120, [int]$SampleSeconds = 600, [switch]$Residence, [string[]]$States = @('pet-idle','pet-sleep','pet-walk','pet-run','house-indoor-idle','house-indoor-moving','house-outdoor-idle','house-outdoor-moving','house-boat-idle','house-boat-playing','floating-chat-idle','floating-chat-streaming-mock','tray-cold','tray-after-100-switches'))
$ErrorActionPreference = 'Stop'
$formeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $formeRoot
$formeSdk = Join-Path $formeRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $formeSdk)) { $formeSdk = (Get-Command dotnet -ErrorAction Stop).Source }
if ($WarmupSeconds -lt 0 -or $WarmupSeconds -gt 600 -or $SampleSeconds -lt 10 -or $SampleSeconds -gt 28800) { throw 'Invalid sampling duration' }
$formeAllowedStates=@('pet-idle','pet-sleep','pet-walk','pet-run','house-indoor-idle','house-indoor-moving','house-outdoor-idle','house-outdoor-moving','house-boat-idle','house-boat-playing','floating-chat-idle','floating-chat-streaming-mock','tray-cold','tray-after-100-switches')
foreach($formeState in $States){if($formeState -notin $formeAllowedStates){throw 'Unknown probe state'}}
& $formeSdk build src/Forme.App/Forme.App.csproj -c Release --no-restore --nologo '-p:FormeDevelopmentTools=true'
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed' }
if($Residence){$SampleSeconds=28800;$States=@('pet-idle')}
$formeEvidence=Join-Path 'artifacts' ('performance-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $formeEvidence -Force | Out-Null
foreach($formeState in $States){
    # Serial fresh processes avoid cross-process CPU contention and state carryover.
    & $formeSdk src/Forme.App/bin/Release/net10.0-windows/Forme.dll --probe --probe-state $formeState --probe-warmup $WarmupSeconds --probe-seconds $SampleSeconds
    if($LASTEXITCODE -ne 0){throw "Probe failed: $formeState"}
    $formeReport=Get-Content -LiteralPath 'artifacts/performance.json' -Raw | ConvertFrom-Json
    if(-not $formeReport.Results[0].PresentationValid){throw "Presentation not valid: $formeState"}
    Copy-Item -LiteralPath 'artifacts/performance.json' -Destination (Join-Path $formeEvidence "$formeState.json")
}
Write-Host "Evidence: $formeEvidence. GPU and mixed-DPI hardware are not measured by this probe."

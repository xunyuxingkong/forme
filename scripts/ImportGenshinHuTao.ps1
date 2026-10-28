param([string]$ModelFile, [string]$DataDirectory = (Join-Path $env:LOCALAPPDATA 'Forme'))
$ErrorActionPreference = 'Stop'
$formeExpected = 'E6E86FB91C28417864ED30305A434541FB9CF214DBCB6F2555AF8795A5E374C7'
$formeModels = Join-Path ([IO.Path]::GetFullPath($DataDirectory)) 'Models'
$formeTarget = Join-Path $formeModels 'genshin-hu-tao.vrm'
New-Item -ItemType Directory -Path $formeModels -Force | Out-Null
$formeTemporary = Join-Path $formeModels ('import-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    if ($ModelFile) {
        Copy-Item -LiteralPath $ModelFile -Destination $formeTemporary
    } else {
        # Explicit manual import only. Forme and its installer never run this
        # download on startup. Metadata permits use, but forbids redistribution.
        Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/AashooSharma/VRM-models/main/Genshin/Hu%20Tao.vrm' -OutFile $formeTemporary -TimeoutSec 60 -UseBasicParsing
    }
    if ((Get-Item -LiteralPath $formeTemporary).Length -ne 7721368 -or (Get-FileHash -LiteralPath $formeTemporary -Algorithm SHA256).Hash -ne $formeExpected) {
        throw 'The downloaded file is not the tested Hu Tao model. Existing model was not changed.'
    }
    Move-Item -LiteralPath $formeTemporary -Destination $formeTarget -Force
    Write-Host "Ready: $formeTarget"
    Write-Host 'Restart Forme, then select Hu Tao under Settings > Companion model and save.'
} finally {
    if (Test-Path -LiteralPath $formeTemporary) { Remove-Item -LiteralPath $formeTemporary -Force }
}

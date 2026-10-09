param([switch]$Package, [switch]$Test)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$formeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $formeRoot
$formeSdk = Join-Path $formeRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $formeSdk)) { $formeSdk = (Get-Command dotnet).Source }
function Invoke-FormeDotnet {
    & $formeSdk @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $LASTEXITCODE" }
}
Invoke-FormeDotnet restore src/Forme.App/Forme.App.csproj --locked-mode
Invoke-FormeDotnet build src/Forme.App/Forme.App.csproj -c Release --no-restore --nologo
if ($Test) {
    Invoke-FormeDotnet run --project tests/Forme.Tests/Forme.Tests.csproj -c Release
    Invoke-FormeDotnet src/Forme.App/bin/Release/net10.0-windows/Forme.dll --smoke
}
if ($Package) {
    $formePublish = Join-Path $formeRoot 'artifacts/publish'
    if (Test-Path -LiteralPath $formePublish) {
        $formeResolved = [IO.Path]::GetFullPath($formePublish)
        if ($formeResolved -ne (Join-Path $formeRoot 'artifacts/publish')) { throw 'Unexpected publish directory' }
        Remove-Item -LiteralPath $formeResolved -Recurse -Force
    }
    Invoke-FormeDotnet publish src/Forme.App/Forme.App.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -o $formePublish
    Copy-Item -LiteralPath 'docs/使用说明.md' -Destination (Join-Path $formePublish 'README.md')
    Copy-Item -LiteralPath 'docs/隐私说明.md' -Destination $formePublish
    Copy-Item -LiteralPath 'THIRD_PARTY_NOTICES.md' -Destination $formePublish
    Copy-Item -LiteralPath 'licenses' -Destination $formePublish -Recurse
    Compress-Archive -Path (Join-Path $formePublish '*') -DestinationPath 'artifacts/payload.zip' -Force
    Invoke-FormeDotnet build src/Forme.Setup/Forme.Setup.csproj -c Release --nologo
    Copy-Item -LiteralPath 'src/Forme.Setup/bin/Release/net48/Forme-Setup.exe' -Destination 'artifacts/Forme-Setup-0.1.0.exe' -Force
    $formeInstaller = Start-Process -FilePath (Join-Path $formeRoot 'artifacts/Forme-Setup-0.1.0.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
    if ($formeInstaller.ExitCode -ne 0) { throw 'Installer payload verification failed' }
    Get-FileHash 'artifacts/Forme-Setup-0.1.0.exe' -Algorithm SHA256 | Format-List | Out-File 'artifacts/sha256.txt' -Encoding UTF8
    Write-Host 'Ready: artifacts/Forme-Setup-0.1.0.exe'
}

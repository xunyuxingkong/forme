param([switch]$Package, [switch]$Test, [switch]$Smoke, [string]$PublishDirectory = 'artifacts/publish')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$formeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $formeRoot
$formeVersion = ([xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)).Project.PropertyGroup.FormeVersion
if ($formeVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Directory.Build.props must define a numeric FormeVersion' }
$formeSdk = Join-Path $formeRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $formeSdk)) { $formeSdk = (Get-Command dotnet).Source }
function Invoke-FormeDotnet {
    & $formeSdk @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $LASTEXITCODE" }
}
Invoke-FormeDotnet restore src/Forme.App/Forme.App.csproj --locked-mode
Invoke-FormeDotnet build src/Forme.App/Forme.App.csproj -c Release --no-restore --nologo
if ($Test) {
    Invoke-FormeDotnet restore tests/Forme.Tests/Forme.Tests.csproj --locked-mode
    Invoke-FormeDotnet test tests/Forme.Tests/Forme.Tests.csproj -c Release --no-restore --logger 'trx;LogFileName=core.trx' --collect 'XPlat Code Coverage' --results-directory artifacts/test-results
}
if ($Smoke) {
    Invoke-FormeDotnet build src/Forme.App/Forme.App.csproj -c Release --no-restore --nologo '-p:FormeDevelopmentTools=true'
    Invoke-FormeDotnet src/Forme.App/bin/Release/net10.0-windows/Forme.dll --smoke
}
if ($Package) {
    $formePublish = [IO.Path]::GetFullPath((Join-Path $formeRoot $PublishDirectory))
    $formeArtifacts = [IO.Path]::GetFullPath((Join-Path $formeRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (-not $formePublish.StartsWith($formeArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish directory must be inside artifacts' }
    if (Test-Path -LiteralPath $formePublish) {
        $formeResolved = [IO.Path]::GetFullPath($formePublish)
        if (-not $formeResolved.StartsWith($formeArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected publish directory' }
        $formeExe = Join-Path $formeResolved 'Forme.exe'
        if (Test-Path -LiteralPath $formeExe) {
            try { $formeCheck = [IO.File]::Open($formeExe, 'Open', 'ReadWrite', 'None'); $formeCheck.Dispose() }
            catch { throw 'This output is running or locked. Use -PublishDirectory artifacts/new-build.' }
        }
        Remove-Item -LiteralPath $formeResolved -Recurse -Force
    }
    Invoke-FormeDotnet publish src/Forme.App/Forme.App.csproj -c Release -r win-x64 --self-contained true --no-restore '-p:DebugType=None' -o $formePublish
    Copy-Item -LiteralPath 'docs/使用说明.md' -Destination (Join-Path $formePublish 'README.md')
    Copy-Item -LiteralPath 'docs/隐私说明.md' -Destination $formePublish
    Copy-Item -LiteralPath 'THIRD_PARTY_NOTICES.md' -Destination $formePublish
    Copy-Item -LiteralPath 'licenses' -Destination $formePublish -Recurse
    Compress-Archive -Path (Join-Path $formePublish '*') -DestinationPath 'artifacts/payload.zip' -Force
    Invoke-FormeDotnet restore src/Forme.Setup/Forme.Setup.csproj --locked-mode
    Invoke-FormeDotnet build src/Forme.Setup/Forme.Setup.csproj -c Release --no-restore --nologo
    $formeInstallerName = "Forme-Setup-$formeVersion.exe"
    Copy-Item -LiteralPath 'src/Forme.Setup/bin/Release/net48/Forme-Setup.exe' -Destination (Join-Path 'artifacts' $formeInstallerName) -Force
    $formeInstaller = Start-Process -FilePath (Join-Path $formeRoot (Join-Path 'artifacts' $formeInstallerName)) -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
    if ($formeInstaller.ExitCode -ne 0) { throw 'Installer payload verification failed' }
    Get-FileHash (Join-Path 'artifacts' $formeInstallerName) -Algorithm SHA256 | Format-List | Out-File 'artifacts/sha256.txt' -Encoding UTF8
    Write-Host "Ready: artifacts/$formeInstallerName"
}

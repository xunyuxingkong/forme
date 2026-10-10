param([string]$ExpectedTag = '', [string]$PublishDirectory = 'artifacts/release-validation-publish')
$ErrorActionPreference = 'Stop'
$formeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $formeRoot
$formeVersion = ([xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)).Project.PropertyGroup.FormeVersion
if ($ExpectedTag -and $ExpectedTag -ne "v$formeVersion") { throw "Tag must match v$formeVersion" }
$formeSdk = Join-Path $formeRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $formeSdk)) { $formeSdk = (Get-Command dotnet -ErrorAction Stop).Source }
& "$PSScriptRoot/build.ps1" -Test
if ($LASTEXITCODE -ne 0) { throw 'Core validation failed' }
& $formeSdk build src/Forme.App/Forme.App.csproj -c Release --no-restore --nologo '-p:FormeDevelopmentTools=true'
if ($LASTEXITCODE -ne 0) { throw 'Development verification build failed' }
function Invoke-FormeCheck([string[]]$CheckArguments, [int]$ExpectedExit, [int]$TimeoutMs) {
    $formeProcess = Start-Process -FilePath $formeSdk -ArgumentList (@('src/Forme.App/bin/Release/net10.0-windows/Forme.dll') + $CheckArguments) -WorkingDirectory $formeRoot -WindowStyle Hidden -PassThru
    if (-not $formeProcess.WaitForExit($TimeoutMs)) { $formeProcess.Kill(); throw 'Verification timed out' }
    if ($formeProcess.ExitCode -ne $ExpectedExit) { throw "Verification failed: $($CheckArguments[0]), exit $($formeProcess.ExitCode)" }
}
Invoke-FormeCheck -CheckArguments @('--smoke') -ExpectedExit 0 -TimeoutMs 240000
$formeCrashDir = Join-Path 'artifacts' ('crash-validation-' + [Guid]::NewGuid().ToString('N'))
Invoke-FormeCheck -CheckArguments @('--crash-test','--data-dir',$formeCrashDir) -ExpectedExit 1 -TimeoutMs 30000
$formeDiagnosticFiles = Get-ChildItem -LiteralPath (Join-Path $formeCrashDir 'diagnostics') -Filter '*.json'
if (-not $formeDiagnosticFiles) { throw 'Missing crash diagnostics' }
foreach ($formeDiagnosticFile in $formeDiagnosticFiles) {
    $formeDiagnostic = Get-Content -LiteralPath $formeDiagnosticFile.FullName -Raw
    if ($formeDiagnostic -match 'secret-token|private-chat') { throw 'Unsafe crash diagnostics' }
    $null = $formeDiagnostic | ConvertFrom-Json
}
& "$PSScriptRoot/build.ps1" -Package -PublishDirectory $PublishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Package verification failed' }
$formePublishedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $formeRoot "$PublishDirectory/Forme.exe")).ProductVersion
if ($formePublishedVersion -ne $formeVersion) { throw 'Published version does not match source version' }
Copy-Item -LiteralPath 'artifacts/payload.zip' -Destination "artifacts/Forme-$formeVersion-win-x64.zip" -Force
$formeFiles = @("artifacts/Forme-Setup-$formeVersion.exe", "artifacts/Forme-$formeVersion-win-x64.zip")
$formeHashes = foreach ($formeFile in $formeFiles) {
    $formeHash = (Get-FileHash -LiteralPath $formeFile -Algorithm SHA256).Hash.ToLowerInvariant()
    "$formeHash  $([IO.Path]::GetFileName($formeFile))"
}
Set-Content -LiteralPath 'artifacts/SHA256SUMS.txt' -Value $formeHashes -Encoding ascii
Write-Host "PASS: build, tests/coverage, WPF smoke, crash privacy, package, installer self-test, version, SHA256 ($formeVersion)."

param([string]$ExpectedTag = '', [string]$PublishDirectory = 'artifacts/release-validation-publish')
$ErrorActionPreference = 'Stop'
$formeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $formeRoot
$formeAppFramework = ([xml](Get-Content -LiteralPath 'src/Forme.App/Forme.App.csproj' -Raw)).SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText
$formeVersion = ([xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)).Project.PropertyGroup.FormeVersion
if ($ExpectedTag -and $ExpectedTag -ne "v$formeVersion") { throw "Tag must match v$formeVersion" }
$formeSdk = Join-Path $formeRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $formeSdk)) { $formeSdk = (Get-Command dotnet -ErrorAction Stop).Source }
& "$PSScriptRoot/build.ps1" -Test
if ($LASTEXITCODE -ne 0) { throw 'Core validation failed' }
& $formeSdk build src/Forme.App/Forme.App.csproj -c Release --no-restore --nologo '-p:FormeDevelopmentTools=true'
if ($LASTEXITCODE -ne 0) { throw 'Development verification build failed' }
function Invoke-FormeCheck([string[]]$CheckArguments, [int]$ExpectedExit, [int]$TimeoutMs) {
    $formeProcess = Start-Process -FilePath $formeSdk -ArgumentList (@("src/Forme.App/bin/Release/$formeAppFramework/Forme.dll") + $CheckArguments) -WorkingDirectory $formeRoot -WindowStyle Hidden -PassThru
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
# Exercise the embedded Windows SDK projection in the actual self-contained executable.
$formePdfInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $formeRoot "$PublishDirectory/Forme.exe"))
$formePdfInfo.UseShellExecute = $false
$formePdfInfo.CreateNoWindow = $true
$formePdfInfo.WindowStyle = 'Hidden'
$formePdfInfo.RedirectStandardInput = $true
$formePdfInfo.RedirectStandardOutput = $true
$formePdfInfo.RedirectStandardError = $true
$formePdfInfo.Arguments = '--render-pdf'
$formePdfProcess = [Diagnostics.Process]::new()
$formePdfProcess.StartInfo = $formePdfInfo
$formePdfWatch = [Diagnostics.Stopwatch]::StartNew()
function Read-FormePdfBytes([int]$Count) {
    $formeBuffer = New-Object byte[] $Count
    $formeOffset = 0
    while ($formeOffset -lt $Count) {
        $formeRemaining = [int][Math]::Max(1, 30000 - $formePdfWatch.ElapsedMilliseconds)
        $formeReading = $formePdfProcess.StandardOutput.BaseStream.ReadAsync($formeBuffer,$formeOffset,$Count-$formeOffset)
        if ($formePdfWatch.ElapsedMilliseconds -ge 30000 -or -not $formeReading.Wait($formeRemaining)) { throw 'Packaged PDF worker timed out' }
        $formeRead = $formeReading.GetAwaiter().GetResult()
        if ($formeRead -le 0) { throw 'Packaged PDF worker returned incomplete data' }
        $formeOffset += $formeRead
    }
    return ,$formeBuffer
}
try {
    if (-not $formePdfProcess.Start()) { throw 'Packaged PDF worker failed to start' }
    $formePdfInput = [IO.File]::ReadAllBytes((Join-Path $formeRoot 'artifacts/pdf-worker-fixture.pdf'))
    $formePdfLength = [BitConverter]::GetBytes([int]$formePdfInput.Length)
    $formePdfProcess.StandardInput.BaseStream.Write($formePdfLength,0,4)
    $formePdfProcess.StandardInput.BaseStream.Write($formePdfInput,0,$formePdfInput.Length)
    $formePdfProcess.StandardInput.Close()
    $formePdfCount = [BitConverter]::ToInt32((Read-FormePdfBytes 4),0)
    if ($formePdfCount -ne 2) { throw 'Packaged PDF worker returned wrong page count' }
    $formePdfTotal = 0
    for ($formePdfPage = 0; $formePdfPage -lt $formePdfCount; $formePdfPage++) {
        $formePdfBytes = [BitConverter]::ToInt32((Read-FormePdfBytes 4),0)
        $formePdfTotal += $formePdfBytes
        if ($formePdfBytes -le 0 -or $formePdfBytes -gt 5242880 -or $formePdfTotal -gt 10485760) { throw 'Packaged PDF worker exceeded bounds' }
        $formePdfPng = Read-FormePdfBytes $formePdfBytes
        if ($formePdfPng[0] -ne 137 -or $formePdfPng[1] -ne 80 -or $formePdfPng[2] -ne 78 -or $formePdfPng[3] -ne 71) { throw 'Packaged PDF worker returned invalid image' }
    }
    if (-not $formePdfProcess.WaitForExit([int][Math]::Max(1,30000-$formePdfWatch.ElapsedMilliseconds)) -or $formePdfProcess.ExitCode -ne 0) { throw 'Packaged PDF worker failed to exit' }
    Set-Content -LiteralPath 'artifacts/pdf-package-worker-result.txt' -Value 'PASS: actual self-contained executable renders two PDF pages via embedded Windows SDK projection; no external calls.'
}
finally {
    try { if (-not $formePdfProcess.HasExited) { $formePdfProcess.Kill() } } catch [InvalidOperationException] { }
    $formePdfProcess.Dispose()
}
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

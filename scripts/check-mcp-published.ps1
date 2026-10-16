param([string]$Executable = 'artifacts/publish/Forme.exe')
$ErrorActionPreference = 'Stop'
$formeCheckRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$formeCheckExe = [IO.Path]::GetFullPath((Join-Path $formeCheckRoot $Executable))
if (-not (Test-Path -LiteralPath $formeCheckExe -PathType Leaf)) { throw 'Published executable not found' }
$formeCheckInfo = [Diagnostics.ProcessStartInfo]::new($formeCheckExe)
$formeCheckInfo.UseShellExecute = $false
$formeCheckInfo.CreateNoWindow = $true
$formeCheckInfo.WindowStyle = 'Hidden'
$formeCheckInfo.RedirectStandardInput = $true
$formeCheckInfo.RedirectStandardOutput = $true
$formeCheckInfo.RedirectStandardError = $true
$formeCheckInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($formeCheckExe)
$formeCheckInfo.Environment.Clear()
foreach ($formeCheckEnvName in @('SystemRoot','WINDIR','TEMP','TMP')) {
    $formeCheckEnvValue = [Environment]::GetEnvironmentVariable($formeCheckEnvName)
    if ($null -ne $formeCheckEnvValue) { $formeCheckInfo.Environment[$formeCheckEnvName] = $formeCheckEnvValue }
}
$formeCheckInfo.ArgumentList.Add('--mcp-web-source')
$formeCheckProcess = [Diagnostics.Process]::new()
$formeCheckProcess.StartInfo = $formeCheckInfo
$formeCheckStarted = $false
function Send-FormeMcp($value) {
    $formeCheckBytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 12 -Compress) + "`n")
    $formeCheckProcess.StandardInput.BaseStream.Write($formeCheckBytes,0,$formeCheckBytes.Length)
    $formeCheckProcess.StandardInput.BaseStream.Flush()
}
function Read-FormeMcp($id) {
    for ($formeCheckAttempt=0;$formeCheckAttempt -lt 8;$formeCheckAttempt++) {
        $formeCheckLine = $formeCheckProcess.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
        if ($null -eq $formeCheckLine) { throw 'MCP stream ended before reply' }
        $formeCheckReply = $formeCheckLine | ConvertFrom-Json
        if ($formeCheckReply.id -eq $id) { return $formeCheckReply }
    }
    throw 'MCP reply not found'
}
try {
    if (-not $formeCheckProcess.Start()) { throw 'MCP worker did not start' }
    $formeCheckStarted = $true
    $formeCheckProcess.StandardInput.BaseStream.WriteByte(0x46)
    $formeCheckProcess.StandardInput.BaseStream.Flush()
    Send-FormeMcp @{jsonrpc='2.0';id=1;method='initialize';params=@{protocolVersion='2025-11-25';capabilities=@{};clientInfo=@{name='FormePublishedCheck';version='1'}}}
    $formeCheckInit = Read-FormeMcp 1
    if ($formeCheckInit.result.serverInfo.name -ne 'Forme.ReadOnlyWeb') { throw 'Unexpected MCP server identity' }
    Send-FormeMcp @{jsonrpc='2.0';method='notifications/initialized'}
    Send-FormeMcp @{jsonrpc='2.0';id=2;method='tools/list'}
    $formeCheckTools = Read-FormeMcp 2
    if (@($formeCheckTools.result.tools).Count -ne 1 -or $formeCheckTools.result.tools[0].name -ne 'forme_read_public_page') { throw 'Unexpected MCP tool manifest' }
    Send-FormeMcp @{jsonrpc='2.0';id=3;method='tools/call';params=@{name='forme_read_public_page';arguments=@{url='http://127.0.0.1/private-check'}}}
    $formeCheckPrivate = Read-FormeMcp 3
    if (-not $formeCheckPrivate.result.isError) { throw 'Published tool did not reject private URL' }
    Send-FormeMcp @{jsonrpc='2.0';id=4;method='tools/call';params=@{name='not_an_allowed_tool';arguments=@{}}}
    $formeCheckUnknown = Read-FormeMcp 4
    if ($null -eq $formeCheckUnknown.error -and -not $formeCheckUnknown.result.isError) { throw 'Published server accepted unknown tool' }
}
finally {
    if ($formeCheckStarted -and -not $formeCheckProcess.HasExited) {
        $formeCheckProcess.Kill($true)
        if (-not $formeCheckProcess.WaitForExit(3000)) { throw 'Published worker did not exit' }
    }
    $formeCheckProcess.Dispose()
}
'PASS: published executable MCP handshake and single tool manifest, private URL and unknown tool rejection, confirmed worker exit. No website/model calls or user-store initialization.' | Set-Content -LiteralPath (Join-Path $formeCheckRoot 'artifacts/mcp-published-result.txt') -Encoding UTF8
Write-Output 'Published MCP check passed'

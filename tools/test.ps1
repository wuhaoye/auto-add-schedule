param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# 与 tools/build.ps1 共用构建锁，避免构建与测试并发写 bin/obj。
$mutexName = "Global\AutoAddScheduleBuild"
$mutex = New-Object System.Threading.Mutex($false, $mutexName)
$acquired = $false
try {
    $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(15))
    if (-not $acquired) {
        Write-Error "等待构建锁超时：另一个构建/测试进程长时间占用。"
        exit 1
    }

    $testArgs = @(
        "tests/AutoAddSchedule.Tests/AutoAddSchedule.Tests.csproj",
        "-c", $Configuration,
        "--nologo",
        "-v", "minimal"
    )
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $testArgs += @("--filter", $Filter)
    }

    & dotnet test @testArgs
    exit $LASTEXITCODE
}
finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

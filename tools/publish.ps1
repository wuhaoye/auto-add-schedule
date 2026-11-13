param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$Output = "publish",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# 与 tools/build.ps1 共用构建锁。
$mutexName = "Global\AutoAddScheduleBuild"
$mutex = New-Object System.Threading.Mutex($false, $mutexName)
$acquired = $false
try {
    $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(20))
    if (-not $acquired) {
        Write-Error "等待构建锁超时：另一个构建进程长时间占用。"
        exit 1
    }
    $publishArgs = @(
        "src/AutoAddSchedule/AutoAddSchedule.csproj",
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", "true",
        "-p:PublishSingleFile=false",
        "-p:DebugType=none",
        "-o", $Output
    )
    & dotnet publish @publishArgs
    exit $LASTEXITCODE
}
finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

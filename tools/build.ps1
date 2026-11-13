param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Verbosity = "minimal"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# 全局构建锁：多进程/多 Agent 并发构建时串行执行，避免 obj/bin 文件占用。
$mutexName = "Global\AutoAddScheduleBuild"
$mutex = New-Object System.Threading.Mutex($false, $mutexName)
$acquired = $false
try {
    $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(10))
    if (-not $acquired) {
        Write-Error "等待构建锁超时：另一个构建进程长时间占用。"
        exit 1
    }
    & dotnet build "AutoAddSchedule.slnx" -c $Configuration -v $Verbosity
    exit $LASTEXITCODE
}
finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

[CmdletBinding()]
param(
    [string]$SqlServer = "localhost,1433",
    [string]$RedisServer = "localhost:6379"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$envFile = Join-Path $repoRoot ".env"

if (-not (Test-Path -LiteralPath $envFile)) {
    throw "Missing .env at $envFile. Create it from .env.example."
}

function Get-DotEnvValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $pattern = "^\s*$([regex]::Escape($Name))\s*="
    $line = Get-Content -LiteralPath $Path |
        Where-Object { $_ -notmatch '^\s*#' -and $_ -match $pattern } |
        Select-Object -First 1

    if (-not $line) {
        return $null
    }

    $value = ($line -replace $pattern, "").Trim()
    if ($value.Length -ge 2) {
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
            ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
    }

    return $value
}

$saPassword = Get-DotEnvValue -Name "SA_PASSWORD" -Path $envFile
if ([string]::IsNullOrWhiteSpace($saPassword)) {
    throw "SA_PASSWORD is not configured in .env."
}

$oldSql = $env:HR_TEST_SQL_CONNECTION_STRING
$oldRedis = $env:HR_TEST_REDIS_CONNECTION_STRING

try {
    $env:HR_TEST_SQL_CONNECTION_STRING =
        "Server=$SqlServer;Database=master;User Id=sa;Password=$saPassword;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5"
    $env:HR_TEST_REDIS_CONNECTION_STRING =
        "$RedisServer,abortConnect=false,connectTimeout=5000"

    $solutions = @(
        "src/hr-service/HrService.slnx",
        "src/leave-service/LeaveService.slnx"
    )

    foreach ($solution in $solutions) {
        $solutionPath = Join-Path $repoRoot $solution
        Write-Host ""
        Write-Host "===== Running tests: $solution =====" -ForegroundColor Cyan

        Push-Location $repoRoot
        try {
            $output = & dotnet test $solutionPath 2>&1
            $exitCode = $LASTEXITCODE
            $output | ForEach-Object { Write-Output $_.ToString() }
        }
        finally {
            Pop-Location
        }

        if ($exitCode -ne 0) {
            throw "dotnet test failed for $solution (exit code $exitCode)."
        }

        $outputText = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        $summary = [regex]::Match(
            $outputText,
            'Test summary:\s*total:\s*(\d+),\s*failed:\s*(\d+),\s*succeeded:\s*(\d+),\s*skipped:\s*(\d+)',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
        )

        if (-not $summary.Success) {
            throw "Test summary not found for $solution; cannot verify skipped = 0."
        }

        $total = [int]$summary.Groups[1].Value
        $failed = [int]$summary.Groups[2].Value
        $skipped = [int]$summary.Groups[4].Value

        if ($failed -ne 0) {
            throw "$solution has $failed failed test(s)."
        }
        if ($skipped -ne 0) {
            throw "$solution has $skipped skipped test(s). Check SQL Server and Redis."
        }

        Write-Host "PASS: $solution - total=$total, failed=0, skipped=0" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Completed: HR Service and Leave Service both have skipped = 0." -ForegroundColor Green
}
finally {
    $env:HR_TEST_SQL_CONNECTION_STRING = $oldSql
    $env:HR_TEST_REDIS_CONNECTION_STRING = $oldRedis
}

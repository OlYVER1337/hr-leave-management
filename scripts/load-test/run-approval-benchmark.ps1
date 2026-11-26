param(
    [ValidateSet(
        "Seed",
        "VerifyConstantVus",
        "VerifyConstantArrivalRate",
        "RunConstantVus",
        "RunConstantArrivalRate"
    )]
    [string]$Mode = "RunConstantArrivalRate",

    [int]$Vus = 0,
    [int]$PreAllocatedVus = 0,
    [int]$MaxVus = 0,

    [string]$ConfigPath = ".\scripts\load-test\approval-benchmark.config.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $repoRoot

if (-not (Test-Path -LiteralPath $ConfigPath)) {
    throw "Không tìm thấy file cấu hình: $ConfigPath"
}

$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$seed = $config.seed
$workload = $config.workload
$composeBaseArgs = @(
    "-p", "hrbench",
    "-f", "docker-compose.yml",
    "-f", "docker-compose.loadtest.yml"
)

function Invoke-Compose {
    param([string[]]$ComposeArguments)

    $allArgs = @($script:composeBaseArgs) + @($ComposeArguments)
    & docker compose @allArgs
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose thất bại (exit code $LASTEXITCODE): $($ComposeArguments -join ' ')"
    }
}

function Get-ExpectedPendingByManager {
    param(
        [ValidateSet("ConstantVus", "ConstantArrivalRate")]
        [string]$WorkloadKind
    )

    $managerCount = [int]$seed.managerCount
    $required = New-Object int[] $managerCount

    if ($WorkloadKind -eq "ConstantVus") {
        $vusToRun = if ($script:Vus -gt 0) { $script:Vus } else { [int]$workload.constantVus.defaultVus }
        $maxPerVu = [int]$workload.constantVus.maxApprovalsPerVu

        if ($vusToRun -lt 1 -or $maxPerVu -lt 1) {
            throw "VUS và maxApprovalsPerVu phải là số nguyên dương."
        }

        for ($vu = 1; $vu -le $vusToRun; $vu++) {
            $managerIndex = ($vu - 1) % $managerCount
            $required[$managerIndex] += $maxPerVu
        }
    }
    else {
        $rate = [int]$workload.constantArrivalRate.ratePerSecond
        $durationSeconds = [int]$workload.durationSeconds
        $plannedIterations = [long]$rate * [long]$durationSeconds

        if ($rate -lt 1 -or $durationSeconds -lt 1 -or $plannedIterations -lt 1) {
            throw "ratePerSecond và durationSeconds phải là số nguyên dương."
        }

        $baseCount = [int][math]::Floor($plannedIterations / $managerCount)
        $remainder = [int]($plannedIterations % $managerCount)
        for ($index = 0; $index -lt $managerCount; $index++) {
            $required[$index] = $baseCount + $(if ($index -lt $remainder) { 1 } else { 0 })
        }
    }

    return ,$required
}

function Get-ImageId {
    param([string]$ServiceName)

    $composeArgs = @($script:composeBaseArgs) + @('ps', '-q', $ServiceName)
    $containerId = & docker compose @composeArgs
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($containerId | Out-String).Trim())) {
        return "not-provided"
    }

    $imageId = & docker inspect --format '{{.Image}}' $containerId
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($imageId | Out-String).Trim())) {
        return "not-provided"
    }

    return ($imageId | Out-String).Trim()
}

function Invoke-Seed {
    $seedFile = ".\scripts\load-test\seed-scale-data.sql"
    Invoke-Compose -ComposeArguments @("cp", $seedFile, "sqlserver:/tmp/seed-scale-data.sql")

    $sqlCommand = '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -d master' +
        " -v EmployeeCount=$([int]$seed.employeeCount)" +
        " RequestsPerEmployee=$([int]$seed.requestsPerEmployee)" +
        " ManagerCount=$([int]$seed.managerCount)" +
        " PendingPercent=$([int]$seed.pendingPercent)" +
        " -i /tmp/seed-scale-data.sql"

    Invoke-Compose -ComposeArguments @(
        "exec", "-T", "sqlserver", "/bin/bash", "-c", $sqlCommand
    )
}

function Invoke-Verify {
    param([int]$ExpectedPendingPerManager)

    $verifyFile = ".\scripts\load-test\verify-approval-seed.sql"
    Invoke-Compose -ComposeArguments @("cp", $verifyFile, "sqlserver:/tmp/verify-approval-seed.sql")

    $sqlCommand = '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -d master' +
        " -v ManagerCount=$([int]$seed.managerCount)" +
        " ExpectedPendingPerManager=$ExpectedPendingPerManager" +
        " -i /tmp/verify-approval-seed.sql"

    Invoke-Compose -ComposeArguments @(
        "exec", "-T", "sqlserver", "/bin/bash", "-c", $sqlCommand
    )
}

function Assert-ApprovalCapacity {
    param([int]$ExpectedPendingPerManager)

    # Approval sustained dùng fixture 50% Pending / 20 đơn mỗi nhân viên.
    if ([int]$seed.pendingPercent -ne 50 -or [int]$seed.requestsPerEmployee -ne 20) {
        throw "Approval workload yêu cầu pendingPercent=50 và requestsPerEmployee=20 trong file cấu hình chung."
    }

    $minimumEmployeesPerManager = [int][math]::Floor(
        [int]$seed.employeeCount / [int]$seed.managerCount
    )
    $pendingPerEmployee = [int]([int]$seed.requestsPerEmployee / 2)
    $seededMinimumPerManager = $minimumEmployeesPerManager * $pendingPerEmployee

    Write-Host "Seed Pending tối thiểu/Manager : $seededMinimumPerManager"
    Write-Host "Pending cần/Manager             : $ExpectedPendingPerManager"

    if ($ExpectedPendingPerManager -gt $seededMinimumPerManager) {
        throw "Seed thiếu capacity: cần $ExpectedPendingPerManager Pending/Manager nhưng cấu hình chỉ tạo ít nhất $seededMinimumPerManager."
    }
}

function Invoke-K6Workload {
    param(
        [ValidateSet("ConstantVus", "ConstantArrivalRate")]
        [string]$WorkloadKind,
        [int]$ExpectedPendingPerManager
    )

    $gitSha = (& git rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($gitSha | Out-String).Trim())) {
        $gitSha = "not-provided"
    }
    else {
        $gitSha = ($gitSha | Out-String).Trim()
    }

    $hrImageId = Get-ImageId -ServiceName "hr-service"
    $leaveImageId = Get-ImageId -ServiceName "leave-service"
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

    if ($WorkloadKind -eq "ConstantVus") {
        $vusToRun = if ($Vus -gt 0) { $Vus } else { [int]$workload.constantVus.defaultVus }
        $maxPerVu = [int]$workload.constantVus.maxApprovalsPerVu
        $reportPrefix = "approval-constant-vus-$vusToRun-$timestamp"

        $runArgs = @(
            "--profile", "loadtest", "run", "--rm",
            "-e", "VUS=$vusToRun",
            "-e", "GIT_SHA=$gitSha",
            "-e", "HR_IMAGE_ID=$hrImageId",
            "-e", "LEAVE_IMAGE_ID=$leaveImageId",
            "-e", "REPORT_JSON=/reports/$reportPrefix-raw.json",
            "-e", "REPORT_SUMMARY_JSON=/reports/$reportPrefix-summary.json",
            "k6", "run", "/scripts/approval-sustained.js"
        )
    }
    else {
        $arrival = $workload.constantArrivalRate
        $preAllocated = if ($PreAllocatedVus -gt 0) {
            $PreAllocatedVus
        } else {
            [int]$arrival.defaultPreAllocatedVUs
        }
        # Mặc định cố định maxVUs bằng preAllocatedVUs để so sánh pool 50/100 rõ ràng.
        $maximum = if ($MaxVus -gt 0) { $MaxVus } else { $preAllocated }

        if ($maximum -lt $preAllocated) {
            throw "MaxVus phải lớn hơn hoặc bằng PreAllocatedVus."
        }

        $reportPrefix = "approval-arrival-rate-$($arrival.ratePerSecond)rps-$preAllocated-vu-pool-$timestamp"
        $runArgs = @(
            "--profile", "loadtest", "run", "--rm",
            "-e", "PREALLOCATED_VUS=$preAllocated",
            "-e", "MAX_VUS=$maximum",
            "-e", "GIT_SHA=$gitSha",
            "-e", "HR_IMAGE_ID=$hrImageId",
            "-e", "LEAVE_IMAGE_ID=$leaveImageId",
            "-e", "REPORT_JSON=/reports/$reportPrefix-raw.json",
            "-e", "REPORT_SUMMARY_JSON=/reports/$reportPrefix-summary.json",
            "k6", "run", "/scripts/approval-arrival-rate.js"
        )
    }

    Write-Host "Git SHA        : $gitSha"
    Write-Host "HR image ID    : $hrImageId"
    Write-Host "Leave image ID : $leaveImageId"
    Write-Host "Report prefix  : $reportPrefix"

    Invoke-Compose -ComposeArguments $runArgs
}

switch ($Mode) {
    "Seed" {
        Invoke-Seed
        break
    }
    "VerifyConstantVus" {
        $required = Get-ExpectedPendingByManager -WorkloadKind "ConstantVus"
        $maxRequired = ($required | Measure-Object -Maximum).Maximum
        Assert-ApprovalCapacity -ExpectedPendingPerManager $maxRequired
        Invoke-Verify -ExpectedPendingPerManager $maxRequired
        break
    }
    "VerifyConstantArrivalRate" {
        $required = Get-ExpectedPendingByManager -WorkloadKind "ConstantArrivalRate"
        $maxRequired = ($required | Measure-Object -Maximum).Maximum
        Assert-ApprovalCapacity -ExpectedPendingPerManager $maxRequired
        Invoke-Verify -ExpectedPendingPerManager $maxRequired
        break
    }
    "RunConstantVus" {
        $required = Get-ExpectedPendingByManager -WorkloadKind "ConstantVus"
        $maxRequired = ($required | Measure-Object -Maximum).Maximum
        Assert-ApprovalCapacity -ExpectedPendingPerManager $maxRequired
        Invoke-Seed
        Invoke-Verify -ExpectedPendingPerManager $maxRequired
        Invoke-K6Workload -WorkloadKind "ConstantVus" -ExpectedPendingPerManager $maxRequired
        break
    }
    "RunConstantArrivalRate" {
        $required = Get-ExpectedPendingByManager -WorkloadKind "ConstantArrivalRate"
        $maxRequired = ($required | Measure-Object -Maximum).Maximum
        Assert-ApprovalCapacity -ExpectedPendingPerManager $maxRequired
        Invoke-Seed
        Invoke-Verify -ExpectedPendingPerManager $maxRequired
        Invoke-K6Workload -WorkloadKind "ConstantArrivalRate" -ExpectedPendingPerManager $maxRequired
        break
    }
}

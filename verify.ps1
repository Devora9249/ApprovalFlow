<#
.SYNOPSIS
    D5 verification script — runs all 4 required journeys plus anti-cheese guards
    against a live ApprovalFlow stack (docker compose up --build) and reports
    PASS/FAIL per journey.

.USAGE
    ./verify.ps1
    ./verify.ps1 -SkipReset          # don't flush Redis state first (state must already be clean)
    ./verify.ps1 -GatewayUrl "http://localhost:5000"
#>

param(
    [string]$GatewayUrl = "http://localhost:5000",
    [string]$IngestionUrl = "http://localhost:5001",
    [string]$DecisionUrl = "http://localhost:5002",
    [string]$PaymentUrl = "http://localhost:5003",
    [int]$PollTimeoutSeconds = 60,
    [int]$PollIntervalSeconds = 2,
    [switch]$SkipReset
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$fixturesPath = Join-Path $scriptRoot "fixtures/sample-invoices.json"

$results = New-Object System.Collections.Generic.List[object]
$autoApprovedNoHumanIds = New-Object System.Collections.Generic.List[string]

function Write-Section($text) {
    Write-Host "`n--- $text ---" -ForegroundColor Cyan
}

function Assert-Status($actual, $expected, $context) {
    if ($actual -ne $expected) {
        throw "$context : expected status '$expected' but got '$actual'"
    }
}

function Submit-Invoice($fixture) {
    $body = $fixture | ConvertTo-Json -Depth 6
    $resp = Invoke-RestMethod -Method Post -Uri "$GatewayUrl/invoices" -Body $body -ContentType "application/json"
    Write-Host "  submitted $($fixture.invoiceNumber) -> trackingId=$($resp.trackingId)"
    return $resp.trackingId
}

function Wait-Resolved($trackingId) {
    $inFlight = @("received", "processing")
    $elapsed = 0
    $state = $null
    while ($elapsed -lt $PollTimeoutSeconds) {
        try {
            $state = Invoke-RestMethod -Method Get -Uri "$GatewayUrl/invoices/$trackingId/status"
        } catch {
            $state = $null
        }
        if ($state -and ($inFlight -notcontains $state.status)) {
            Write-Host "  resolved $trackingId -> status=$($state.status)"
            return $state
        }
        Start-Sleep -Seconds $PollIntervalSeconds
        $elapsed += $PollIntervalSeconds
    }
    $lastStatus = if ($state) { $state.status } else { "unknown/404" }
    throw "Timed out after ${PollTimeoutSeconds}s waiting for $trackingId to resolve (last status: $lastStatus)"
}

function Wait-PaymentResolved($trackingId) {
    $elapsed = 0
    $state = $null
    while ($elapsed -lt $PollTimeoutSeconds) {
        $state = Invoke-RestMethod -Method Get -Uri "$GatewayUrl/invoices/$trackingId/status"
        if ($state.paymentStatus) {
            Write-Host "  payment resolved $trackingId -> paymentStatus=$($state.paymentStatus)"
            return $state
        }
        Start-Sleep -Seconds $PollIntervalSeconds
        $elapsed += $PollIntervalSeconds
    }
    throw "Timed out after ${PollTimeoutSeconds}s waiting for payment result on $trackingId (status=$($state.status), paymentStatus=$($state.paymentStatus))"
}

function Approve-Invoice($trackingId, $approverId = "verify-script") {
    $body = @{ action = "approve"; approverId = $approverId } | ConvertTo-Json
    return Invoke-RestMethod -Method Post -Uri "$GatewayUrl/invoices/$trackingId/decision" -Body $body -ContentType "application/json"
}

function Get-Fixture($invoiceNumber) {
    $match = $fixtures | Where-Object { $_.invoiceNumber -eq $invoiceNumber } | Select-Object -First 1
    if (-not $match) { throw "No fixture found for $invoiceNumber in $fixturesPath" }
    return $match
}

function Run-Journey($name, [scriptblock]$block) {
    Write-Section $name
    try {
        & $block
        Write-Host "PASS $name" -ForegroundColor Green
        $results.Add([pscustomobject]@{ Name = $name; Passed = $true; Detail = "" })
    } catch {
        Write-Host "FAIL $name -- $($_.Exception.Message)" -ForegroundColor Red
        $results.Add([pscustomobject]@{ Name = $name; Passed = $false; Detail = $_.Exception.Message })
    }
}

# --- Preflight: live stack must be up ---
Write-Section "Preflight: health checks"
$healthTargets = @(
    @{ Name = "gateway"; Url = "$GatewayUrl/health" },
    @{ Name = "ingestion-service"; Url = "$IngestionUrl/health" },
    @{ Name = "decision-service"; Url = "$DecisionUrl/health" },
    @{ Name = "payment-service"; Url = "$PaymentUrl/health" }
)
$unhealthy = @()
foreach ($target in $healthTargets) {
    try {
        Invoke-RestMethod -Method Get -Uri $target.Url | Out-Null
        Write-Host "  $($target.Name): healthy"
    } catch {
        Write-Host "  $($target.Name): UNREACHABLE ($($target.Url))" -ForegroundColor Red
        $unhealthy += $target.Name
    }
}
if ($unhealthy.Count -gt 0) {
    Write-Host "`nStack is not fully up: $($unhealthy -join ', ')." -ForegroundColor Red
    Write-Host "Run 'docker compose up --build' and wait for all services to report healthy, then re-run this script." -ForegroundColor Red
    exit 1
}

if (-not $SkipReset) {
    Write-Section "Resetting state store (docker compose exec redis redis-cli FLUSHALL)"
    try {
        docker compose exec -T redis redis-cli FLUSHALL | Out-Null
        Write-Host "  Redis flushed — starting from a clean slate."
    } catch {
        Write-Host "  Could not flush Redis automatically ($($_.Exception.Message)) — continuing anyway. Use -SkipReset to silence this." -ForegroundColor Yellow
    }
}

if (-not (Test-Path $fixturesPath)) {
    Write-Host "Fixtures file not found at $fixturesPath" -ForegroundColor Red
    exit 1
}
$fixtures = Get-Content $fixturesPath -Raw | ConvertFrom-Json

# --- Journey A: auto approve ---
Run-Journey "Journey A - Auto approve (INV-1001)" {
    $fixture = Get-Fixture "INV-1001"
    $trackingId = Submit-Invoice $fixture
    $state = Wait-Resolved $trackingId
    Assert-Status $state.status "auto_approved" "Journey A"
    $autoApprovedNoHumanIds.Add($trackingId)
}

# --- Journey B: escalate + human approves ---
Run-Journey "Journey B - Escalate then human approves (INV-1003)" {
    $fixture = Get-Fixture "INV-1003"
    $trackingId = Submit-Invoice $fixture
    $state = Wait-Resolved $trackingId
    Assert-Status $state.status "waiting_for_human" "Journey B (pre-decision)"
    $decided = Approve-Invoice $trackingId
    Assert-Status $decided.status "approved" "Journey B (post-decision)"
}

# --- Journey C: duplicate blocked ---
Run-Journey "Journey C - Duplicate submission blocked (INV-1001 resubmitted)" {
    $fixture = Get-Fixture "INV-1001"
    $firstId = Submit-Invoice $fixture
    Wait-Resolved $firstId | Out-Null
    $secondId = Submit-Invoice $fixture
    $secondState = Wait-Resolved $secondId
    Assert-Status $secondState.status "duplicate" "Journey C (second submission)"
}

# --- Journey D: payment failure + compensation ---
Run-Journey "Journey D - Payment failure and compensation (INV-1012)" {
    $fixture = Get-Fixture "INV-1012"
    $trackingId = Submit-Invoice $fixture
    $state = Wait-Resolved $trackingId
    Assert-Status $state.status "waiting_for_human" "Journey D (pre-decision)"
    $decided = Approve-Invoice $trackingId
    Assert-Status $decided.status "approved" "Journey D (post-decision)"
    $paid = Wait-PaymentResolved $trackingId
    Assert-Status $paid.paymentStatus "payment-failed" "Journey D (payment result)"
}

# --- Anti-cheese: prompt injection still escalates ---
Run-Journey "Anti-cheese - prompt injection still escalates (INV-1013)" {
    $fixture = Get-Fixture "INV-1013"
    $trackingId = Submit-Invoice $fixture
    $state = Wait-Resolved $trackingId
    if ($state.status -eq "auto_approved") {
        throw "INV-1013 auto-approved despite 'approve me' prompt injection in the description — Layer 3 was bypassed"
    }
    Assert-Status $state.status "waiting_for_human" "Anti-cheese INV-1013"
}

# --- Anti-cheese: a second, independent auto-approve with no human ---
Run-Journey "Anti-cheese - second auto-approve with no human (INV-1016)" {
    $fixture = Get-Fixture "INV-1016"
    $trackingId = Submit-Invoice $fixture
    $state = Wait-Resolved $trackingId
    Assert-Status $state.status "auto_approved" "Anti-cheese INV-1016"
    $autoApprovedNoHumanIds.Add($trackingId)
}

# --- Anti-cheese: at least 2 auto-approvals with zero human involvement ---
Run-Journey "Anti-cheese - at least 2 invoices auto-approved with no human" {
    if ($autoApprovedNoHumanIds.Count -lt 2) {
        throw "Only $($autoApprovedNoHumanIds.Count) invoice(s) auto-approved with no human involvement (need >= 2)"
    }
    Write-Host "  auto-approved with no human involvement: $($autoApprovedNoHumanIds -join ', ')"
}

# --- Summary ---
# Windows PowerShell 5.1 has no `u{} escape (that's PS 6+ only), so build the
# emoji via ConvertFromUtf32 instead.
$checkMark = [char]::ConvertFromUtf32(0x2705)
$crossMark = [char]::ConvertFromUtf32(0x274C)

Write-Host "`n================ SUMMARY ================" -ForegroundColor Cyan
foreach ($r in $results) {
    if ($r.Passed) {
        Write-Host "PASS $checkMark $($r.Name)" -ForegroundColor Green
    } else {
        Write-Host "FAIL $crossMark $($r.Name)" -ForegroundColor Red
        Write-Host "       $($r.Detail)" -ForegroundColor DarkRed
    }
}

$failed = $results | Where-Object { -not $_.Passed }
if ($failed.Count -eq 0) {
    Write-Host "`nALL JOURNEYS PASSED" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nSOME JOURNEYS FAILED" -ForegroundColor Red
    exit 1
}

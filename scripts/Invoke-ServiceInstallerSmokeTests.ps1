[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ServicesRoot,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [string[]]$ServiceNames = @('docTR', 'EasyOCR', 'MangaOCR', 'PaddleOCR', 'Qwen3TTS')
)

$ErrorActionPreference = 'Stop'
$ServicesRoot = [System.IO.Path]::GetFullPath($ServicesRoot)
$ResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory)

if (-not (Test-Path -LiteralPath $ServicesRoot -PathType Container)) {
    throw "Services directory not found: $ServicesRoot"
}

New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
$results = New-Object System.Collections.Generic.List[object]

foreach ($serviceName in $ServiceNames) {
    $serviceDirectory = Join-Path $ServicesRoot $serviceName
    $installer = Join-Path $serviceDirectory 'Install.bat'
    $diagnostic = Join-Path $serviceDirectory 'DiagnosticTest.bat'

    if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
        throw "Installer not found: $installer"
    }

    Write-Host "============================================================"
    Write-Host "REAL INSTALL TEST: $serviceName"
    Write-Host "============================================================"

    $startedAt = Get-Date
    $installConsoleLog = Join-Path $ResultsDirectory "$serviceName-install-console.txt"

    Push-Location $serviceDirectory
    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & $env:ComSpec /d /c 'call Install.bat nopause' 2>&1 |
            ForEach-Object { $_.ToString() } |
            Tee-Object -FilePath $installConsoleLog
        $installExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        Pop-Location
    }

    $setupLog = Join-Path $serviceDirectory 'setup_log.txt'
    if (Test-Path -LiteralPath $setupLog -PathType Leaf) {
        Copy-Item -LiteralPath $setupLog -Destination (Join-Path $ResultsDirectory "$serviceName-setup_log.txt") -Force
    }

    $diagnosticExitCode = $null
    if ($installExitCode -eq 0 -and (Test-Path -LiteralPath $diagnostic -PathType Leaf)) {
        Write-Host "Running diagnostics for $serviceName..."
        $diagnosticConsoleLog = Join-Path $ResultsDirectory "$serviceName-diagnostic-console.txt"

        Push-Location $serviceDirectory
        try {
            $previousErrorActionPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            & $env:ComSpec /d /c 'call DiagnosticTest.bat nopause' 2>&1 |
                ForEach-Object { $_.ToString() } |
                Tee-Object -FilePath $diagnosticConsoleLog
            $diagnosticExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
            Pop-Location
        }
    }

    $finishedAt = Get-Date
    $result = [pscustomobject]@{
        Service = $serviceName
        InstallExitCode = $installExitCode
        DiagnosticExitCode = $diagnosticExitCode
        DurationMinutes = [math]::Round(($finishedAt - $startedAt).TotalMinutes, 2)
        Passed = ($installExitCode -eq 0 -and $diagnosticExitCode -eq 0)
    }
    $results.Add($result)
    $result | Format-List | Out-Host
}

$summaryPath = Join-Path $ResultsDirectory 'summary.json'
$results | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding UTF8
$results | Format-Table -AutoSize | Out-Host

if ($results.Where({ -not $_.Passed }).Count -gt 0) {
    Write-Error "One or more real installer tests failed. Results: $summaryPath"
    exit 1
}

Write-Host "PASS: all real installer and diagnostic tests succeeded. Results: $summaryPath"

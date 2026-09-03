[CmdletBinding()]
param(
    [switch]$KeepTemporaryFiles,
    [string]$ServicesRoot
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ServicesRoot)) {
    $ServicesRoot = Join-Path $repoRoot 'app\services'
}
$ServicesRoot = [System.IO.Path]::GetFullPath($ServicesRoot)
$serviceNames = @('docTR', 'EasyOCR', 'MangaOCR', 'PaddleOCR', 'Qwen3TTS')
$gpuCases = @(
    @{ Name = 'NVIDIA GeForce RTX 2080 Ti'; Expected = '30_40' },
    @{ Name = 'NVIDIA GeForce RTX 3070'; Expected = '30_40' },
    @{ Name = 'NVIDIA GeForce RTX 4090'; Expected = '30_40' },
    @{ Name = 'NVIDIA GeForce RTX 5090'; Expected = '50' }
)

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('UGTLive-ServiceInstallerTests-' + [Guid]::NewGuid().ToString('N'))
$tempRoot = [System.IO.Path]::GetFullPath($tempRoot)
$systemTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())

if (-not $tempRoot.StartsWith($systemTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use unexpected temporary test path: $tempRoot"
}

function Invoke-InstrumentedDetection {
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [Parameter(Mandatory = $true)][string]$GpuName,
        [Parameter(Mandatory = $true)][string]$ExpectedSeries,
        [string]$ForcedSeries
    )

    $serviceTemp = Join-Path $tempRoot $ServiceName
    $mockBin = Join-Path $tempRoot 'mock-bin'
    $installerSource = Join-Path $ServicesRoot "$ServiceName\Install.bat"
    $configSource = Join-Path $ServicesRoot "$ServiceName\service_config.txt"
    $installerCopy = Join-Path $serviceTemp 'Install.bat'

    New-Item -ItemType Directory -Path $serviceTemp -Force | Out-Null
    Copy-Item -LiteralPath $configSource -Destination (Join-Path $serviceTemp 'service_config.txt') -Force

    # Exit immediately after the production GPU-detection block. This exercises the
    # real batch logic without creating a venv or downloading any dependencies.
    $installerText = [System.IO.File]::ReadAllText($installerSource)
    $nvidiaSmiLine = [regex]::new('(?m)^nvidia-smi --query-gpu=name[^\r\n]*(?=\r?$)')
    if ($nvidiaSmiLine.Matches($installerText).Count -ne 1) {
        throw "$ServiceName installer has an unexpected nvidia-smi detection command"
    }
    $installerText = $nvidiaSmiLine.Replace(
        $installerText,
        '> "%SCRIPT_DIR%gpu_check_temp.txt" echo(!MOCK_GPU_NAME!',
        1)

    $skipLabel = [regex]::new('(?m)^:SkipGPUDetect\r?$')
    if (-not $skipLabel.IsMatch($installerText)) {
        throw "$ServiceName installer has no :SkipGPUDetect label"
    }

    $testExit = ":SkipGPUDetect`r`necho TEST_GPU_SERIES=!GPU_SERIES!`r`nexit /b 0"
    $instrumentedText = $skipLabel.Replace($installerText, $testExit, 1)
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($installerCopy, $instrumentedText, $utf8WithoutBom)

    $oldPath = $env:PATH
    $oldMockGpuName = $env:MOCK_GPU_NAME
    $env:PATH = "$mockBin;$oldPath"
    $env:MOCK_GPU_NAME = $GpuName

    Push-Location $serviceTemp
    try {
        $arguments = if ([string]::IsNullOrEmpty($ForcedSeries)) {
            'call Install.bat nopause'
        }
        else {
            "call Install.bat nopause $ForcedSeries"
        }

        $output = (& $env:ComSpec /d /c $arguments 2>&1 | Out-String)
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
        $env:PATH = $oldPath
        $env:MOCK_GPU_NAME = $oldMockGpuName
    }

    if ($exitCode -ne 0) {
        throw "$ServiceName detection test exited with $exitCode.`n$output"
    }

    if ($output -match 'SHADOW_FIND_INVOKED') {
        throw "$ServiceName invoked the PATH-shadowed find command.`n$output"
    }

    $expectedOutput = "TEST_GPU_SERIES=$ExpectedSeries"
    if ($output -notmatch [regex]::Escape($expectedOutput)) {
        throw "$ServiceName classified '$GpuName' incorrectly. Expected $ExpectedSeries.`n$output"
    }
}

function Invoke-InstrumentedInstallBranch {
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [Parameter(Mandatory = $true)][ValidateSet('30_40', '50')][string]$Series,
        [Parameter(Mandatory = $true)][int]$MockPythonExitCode
    )

    $serviceTemp = Join-Path $tempRoot "$ServiceName-branch"
    $mockBin = Join-Path $tempRoot 'mock-bin'
    $installerSource = Join-Path $ServicesRoot "$ServiceName\Install.bat"
    $installerCopy = Join-Path $serviceTemp 'Install.bat'

    New-Item -ItemType Directory -Path $serviceTemp -Force | Out-Null

    # Enter the selected production subroutine before environment creation. The
    # mocked Python executable makes both success and failure paths deterministic.
    $installerText = [System.IO.File]::ReadAllText($installerSource)
    $setLocalLine = [regex]::new('(?m)^setlocal ENABLEDELAYEDEXPANSION\r?$')
    if (-not $setLocalLine.IsMatch($installerText)) {
        throw "$ServiceName installer has no delayed-expansion setup line"
    }

    $testEntry = @'
setlocal ENABLEDELAYEDEXPANSION
set "NOPAUSE=nopause"
set "LOG_FILE=nul"
if /I "%~1"=="test-30_40" (
    call :Install30_40Series
    exit /b !errorlevel!
)
if /I "%~1"=="test-50" (
    call :Install50Series
    exit /b !errorlevel!
)
'@
    $instrumentedText = $setLocalLine.Replace($installerText, $testEntry, 1)
    $pythonCommandLine = [regex]::new('(?im)^(?<indent>[ \t]*)python(?:\.exe)?\s+.*$')
    $instrumentedText = $pythonCommandLine.Replace(
        $instrumentedText,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            $match.Groups['indent'].Value + '"%ComSpec%" /d /c exit !MOCK_COMMAND_EXIT_CODE!'
        })
    $pythonForCommandLine = [regex]::new('(?im)^(?<indent>[ \t]*)for /f[^\r\n]*\(''python[^\r\n]*(?=\r?$)')
    $instrumentedText = $pythonForCommandLine.Replace(
        $instrumentedText,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            $match.Groups['indent'].Value + 'rem Mocked Python query'
        })
    if ($instrumentedText -match '(?im)^[ \t]*for /f[^\r\n]*\(''python') {
        throw "$ServiceName branch instrumentation left a live Python query"
    }
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($installerCopy, $instrumentedText, $utf8WithoutBom)

    $oldMockExitCode = $env:MOCK_COMMAND_EXIT_CODE
    $env:MOCK_COMMAND_EXIT_CODE = $MockPythonExitCode.ToString()

    Push-Location $serviceTemp
    try {
        $output = (& $env:ComSpec /d /c "call Install.bat test-$Series" 2>&1 | Out-String)
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
        $env:MOCK_COMMAND_EXIT_CODE = $oldMockExitCode
    }

    if ($exitCode -ne $MockPythonExitCode) {
        throw "$ServiceName RTX $Series branch returned $exitCode with mocked Python exit code $MockPythonExitCode.`n$output"
    }
}

try {
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    $mockBin = Join-Path $tempRoot 'mock-bin'
    New-Item -ItemType Directory -Path $mockBin | Out-Null

    $mockFind = Join-Path $mockBin 'find.cmd'
    $mockFindText = "@echo off`r`necho SHADOW_FIND_INVOKED 1^>^&2`r`nexit /b 99`r`n"
    [System.IO.File]::WriteAllText($mockFind, $mockFindText, [System.Text.Encoding]::ASCII)

    foreach ($serviceName in $serviceNames) {
        $installerPath = Join-Path $ServicesRoot "$serviceName\Install.bat"
        $installerText = [System.IO.File]::ReadAllText($installerPath)

        if ($installerText -match '(?im)\|\s*find(?:\.exe)?\s') {
            throw "$serviceName still contains a PATH-sensitive find pipeline"
        }

        if ($installerText -match '(?im)^\s*pause\s*$') {
            throw "$serviceName still contains an unconditional pause command"
        }

        if ($installerText -notmatch '(?im)^:Install30_40Series\s*$' -or
            $installerText -notmatch '(?im)^:Install50Series\s*$') {
            throw "$serviceName is missing an RTX installation branch"
        }

        if ($serviceName -eq 'EasyOCR') {
            $modelDownloadCommands = @($installerText -split "`r?`n" | Where-Object { $_ -match 'easyocr\.Reader' })
            if ($modelDownloadCommands.Count -ne 2 -or
                @($modelDownloadCommands | Where-Object { $_ -notmatch '^python -X utf8 -c ' }).Count -ne 0) {
                throw 'EasyOCR model downloads must force UTF-8 output in both RTX branches'
            }
        }

        if ($serviceName -eq 'MangaOCR' -and
            $installerText -notmatch 'torch==2\.6\.0\+cu118\s+torchvision==0\.21\.0\+cu118\s+torchaudio==2\.6\.0\+cu118') {
            throw 'MangaOCR RTX 30/40 dependencies must stay pinned to the compatible CUDA 11.8 versions'
        }

        foreach ($gpuCase in $gpuCases) {
            Invoke-InstrumentedDetection -ServiceName $serviceName -GpuName $gpuCase.Name -ExpectedSeries $gpuCase.Expected
        }

        Invoke-InstrumentedDetection -ServiceName $serviceName -GpuName 'ignored' -ExpectedSeries '30_40' -ForcedSeries '30_40'
        Invoke-InstrumentedDetection -ServiceName $serviceName -GpuName 'ignored' -ExpectedSeries '50' -ForcedSeries '50'

        Invoke-InstrumentedInstallBranch -ServiceName $serviceName -Series '30_40' -MockPythonExitCode 0
        Invoke-InstrumentedInstallBranch -ServiceName $serviceName -Series '50' -MockPythonExitCode 0
        Invoke-InstrumentedInstallBranch -ServiceName $serviceName -Series '30_40' -MockPythonExitCode 1
        Invoke-InstrumentedInstallBranch -ServiceName $serviceName -Series '50' -MockPythonExitCode 1
    }

    Write-Host "PASS: validated GPU detection, forced RTX routing, both install branches, PATH shadowing, and nopause failure handling for $($serviceNames.Count) installers."
}
finally {
    if ($KeepTemporaryFiles) {
        Write-Host "Kept temporary test files at $tempRoot"
    }
    elseif (Test-Path -LiteralPath $tempRoot) {
        $resolvedTempRoot = [System.IO.Path]::GetFullPath($tempRoot)
        if (-not $resolvedTempRoot.StartsWith($systemTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected test path: $resolvedTempRoot"
        }

        Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
    }
}

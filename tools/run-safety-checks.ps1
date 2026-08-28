param(
    [switch]$Ci,
    [switch]$UseStubApi
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultRoot = Join-Path $repoRoot "TestResults"
$runRoot = Join-Path $resultRoot "safety-$timestamp"
$logsRoot = Join-Path $runRoot "logs"
$deployRoot = Join-Path $runRoot "Scripts"
$gtaRoot = $null

New-Item -ItemType Directory -Force -Path $runRoot, $logsRoot, $deployRoot | Out-Null

$script:SafetyCollectorInvoked = $false

function Invoke-SafetyFailureCollection {
    param([string]$FailureText)

    if ($script:SafetyCollectorInvoked) {
        return
    }

    $script:SafetyCollectorInvoked = $true
    $collector = Join-Path $repoRoot "tools\collect-bug-logs.ps1"

    if (-not (Test-Path -LiteralPath $collector -PathType Leaf)) {
        Write-Warning "Collecteur de logs introuvable: $collector"
        return
    }

    try {
        $failurePath = Join-Path $runRoot "safety-failure.txt"
        $FailureText | Set-Content -LiteralPath $failurePath -Encoding UTF8

        $collectorArguments = @(
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            $collector,
            "-Title",
            "safety-failure",
            "-SinceHours",
            "24"
        )

        if ($gtaRoot) {
            $collectorArguments += @("-GtaRoot", $gtaRoot)
        }

        & powershell @collectorArguments 2>&1 |
            Tee-Object -FilePath (Join-Path $logsRoot "collect-bug-logs.log")
    }
    catch {
        Write-Warning "Collecte automatique des logs impossible: $($_.Exception.Message)"
    }
}

trap {
    Invoke-SafetyFailureCollection ($_ | Out-String)
    throw $_
}

function Invoke-LoggedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StepName,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $logPath = Join-Path $logsRoot "$StepName.log"
    $displayArgs = $Arguments -join " "
    Write-Host "[$StepName] $FilePath $displayArgs"

    & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $logPath
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "La verification '$StepName' a echoue avec le code $exitCode. Log: $logPath"
    }
}

if ($UseStubApi) {
    $stubProject = Join-Path $repoRoot "tools\Stubs\NIBScriptHookVDotNet2\NIBScriptHookVDotNet2.csproj"
    $stubOutput = Join-Path $runRoot "stub-api"
    $gtaRoot = Join-Path $runRoot "stub-gta"

    New-Item -ItemType Directory -Force -Path $stubOutput, $gtaRoot | Out-Null
    New-Item -ItemType File -Force -Path (Join-Path $gtaRoot "GTA5_Enhanced.exe") | Out-Null

    Invoke-LoggedCommand `
        -StepName "build-stub-api" `
        -FilePath "dotnet" `
        -Arguments @("build", $stubProject, "-c", "Release", "-o", $stubOutput)

    Copy-Item `
        -LiteralPath (Join-Path $stubOutput "NIBScriptHookVDotNet2.dll") `
        -Destination (Join-Path $gtaRoot "NIBScriptHookVDotNet2.dll") `
        -Force
}

$msbuildProperties = @("/p:GtaScriptsDir=$deployRoot")

if ($gtaRoot) {
    $msbuildProperties += "/p:GtaRoot=$gtaRoot"
}
if ($UseStubApi) {
    # Je rends le backend simulé visible uniquement aux tests compilés contre le stub.
    $msbuildProperties += "/p:UseStubApi=true"
}

Invoke-LoggedCommand `
    -StepName "restore" `
    -FilePath "dotnet" `
    -Arguments (@("restore", (Join-Path $repoRoot "GTA5modDEV.sln")) + $msbuildProperties)

Invoke-LoggedCommand `
    -StepName "build-release" `
    -FilePath "dotnet" `
    -Arguments (@("build", (Join-Path $repoRoot "GTA5modDEV.sln"), "-c", "Release", "--no-restore") + $msbuildProperties)

Invoke-LoggedCommand `
    -StepName "test-release" `
    -FilePath "dotnet" `
    -Arguments (@(
        "test",
        (Join-Path $repoRoot "GTA5modDEV.sln"),
        "-c",
        "Release",
        "--no-build",
        "--logger",
        "trx;LogFileName=safety-tests.trx",
        "--results-directory",
        $runRoot
    ) + $msbuildProperties)

$mainBin = Join-Path $repoRoot "src\DonJEnemySpawner\bin\Release"
$expectedFiles = @(
    (Join-Path $mainBin "DonJCustomNpcPlacer.dll"),
    (Join-Path $mainBin "DonJCustomNpcPlacer.ENdll"),
    (Join-Path $mainBin "DonJCustomNpcPlacer.pdb"),
    (Join-Path $deployRoot "DonJCustomNpcPlacer.ENdll")
)

foreach ($file in $expectedFiles) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Fichier attendu introuvable apres validation: $file"
    }
}

$forbiddenDeployedFiles = @(
    "DonJEnemySpawner.dll",
    "DonJEnemySpawner.ENdll",
    "DonJEnemySpawner.pdb"
)

foreach ($fileName in $forbiddenDeployedFiles) {
    $candidate = Join-Path $deployRoot $fileName

    if (Test-Path -LiteralPath $candidate) {
        throw "Ancien fichier interdit encore present dans le dossier de deploiement temporaire: $candidate"
    }
}

$summaryPath = Join-Path $runRoot "summary.txt"
@(
    "Statut: OK",
    "CI: $Ci",
    "Stub API: $UseStubApi",
    "Dossier resultats: $runRoot",
    "Dossier deploiement temporaire: $deployRoot",
    "Verification: restore + build Release + tests Release + contrat ENdll"
) | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host "Suite securite OK. Resultats: $runRoot"

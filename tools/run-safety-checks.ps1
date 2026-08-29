param(
    [switch]$Ci,
    [switch]$UseStubApi,
    [string]$PackageOutputDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultRoot = Join-Path $repoRoot "TestResults"
$runRoot = Join-Path $resultRoot "safety-$timestamp"
$logsRoot = Join-Path $runRoot "logs"
$temporaryGtaRoot = Join-Path $runRoot "temporary-gta"
$deployRoot = Join-Path $temporaryGtaRoot "Scripts"
if ([string]::IsNullOrWhiteSpace($PackageOutputDirectory)) {
    $packageRoot = Join-Path $runRoot "game-ready"
}
elseif ([System.IO.Path]::IsPathRooted($PackageOutputDirectory)) {
    $packageRoot = [System.IO.Path]::GetFullPath($PackageOutputDirectory)
}
else {
    $packageRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $repoRoot $PackageOutputDirectory))
}
$gtaRoot = $null

New-Item -ItemType Directory -Force -Path $runRoot, $logsRoot, $temporaryGtaRoot, $deployRoot | Out-Null
New-Item -ItemType File -Force -Path (Join-Path $temporaryGtaRoot "GTA5_Enhanced.exe") | Out-Null

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

$implicitDeploymentNames = @(
    "DonJCustomNpcPlacer.ENdll",
    "DonJCustomNpcPlacer.pdb",
    "DonJCustomNpcPlacer.manifest.json"
)
foreach ($fileName in $implicitDeploymentNames) {
    $implicitDeployment = Join-Path $deployRoot $fileName
    if (Test-Path -LiteralPath $implicitDeployment -PathType Leaf) {
        throw "La compilation Release a deploye implicitement dans GTA: $implicitDeployment"
    }
}

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
$testBin = Join-Path $repoRoot "tests\DonJEnemySpawner.Tests\bin\Release"
$packageScript = Join-Path $repoRoot "tools\package-game-ready.ps1"
$deployScript = Join-Path $repoRoot "tools\deploy-game-ready.ps1"

$packageArguments = @(
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    $packageScript,
    "-Configuration",
    "Release",
    "-RepositoryRoot",
    $repoRoot,
    "-BuildDirectory",
    $mainBin,
    "-OutputDirectory",
    $packageRoot,
    "-DependencyDirectory",
    $testBin
)
if (-not $Ci) {
    # Je permets seulement au contrôle local de produire un artefact explicitement
    # marqué non publiable; la CI et le déploiement restent stricts.
    $packageArguments += "-AllowDirtySource"
}

Invoke-LoggedCommand `
    -StepName "package-game-ready" `
    -FilePath "powershell" `
    -Arguments $packageArguments

$manifestPath = Join-Path $packageRoot "manifest.json"
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$packageIsPublishable = -not [bool]$manifest.sourceDirty

$forbiddenDeployedFiles = @(
    "DonJCustomNpcPlacer.dll",
    "DonJEnemySpawner.dll",
    "DonJEnemySpawner.ENdll",
    "DonJEnemySpawner.pdb"
)
$deployArguments = @(
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    $deployScript,
    "-PackageDirectory",
    $packageRoot,
    "-GtaRoot",
    $temporaryGtaRoot,
    "-GtaScriptsDir",
    $deployRoot
)

if ($packageIsPublishable) {
    # Je pose des sentinelles seulement après la preuve que le build normal n'a
    # rien déployé, puis j'exerce le remplacement explicite et son nettoyage.
    [System.IO.File]::WriteAllBytes(
        (Join-Path $deployRoot "DonJCustomNpcPlacer.ENdll"),
        [byte[]](0x44, 0x4A, 0x4F, 0x4C, 0x44))
    [System.IO.File]::WriteAllBytes(
        (Join-Path $deployRoot "DonJCustomNpcPlacer.pdb"),
        [byte[]](0x50, 0x44, 0x42))
    Set-Content `
        -LiteralPath (Join-Path $deployRoot "DonJCustomNpcPlacer.manifest.json") `
        -Value '{"ancien":true}' `
        -Encoding ASCII
    foreach ($fileName in $forbiddenDeployedFiles) {
        Set-Content -LiteralPath (Join-Path $deployRoot $fileName) -Value "ancien-alias" -Encoding ASCII
    }

    Invoke-LoggedCommand `
        -StepName "deploy-game-ready" `
        -FilePath "powershell" `
        -Arguments $deployArguments
}
else {
    # Je laisse le sous-processus refuser le package sale sans que son stderr
    # attendu court-circuite l'assertion de son code de sortie.
    $dirtyDeployExitCode = -1
    $safetyErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & powershell @deployArguments *> (Join-Path $runRoot "deploy-dirty-refusal.log")
        $dirtyDeployExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $safetyErrorActionPreference
    }
    if ($dirtyDeployExitCode -eq 0) {
        throw "Le déploiement a accepté un package local marqué sourceDirty=true."
    }
    Write-Host "Déploiement sale refusé comme prévu; validation complète réservée à la CI propre."
}

$expectedFiles = @(
    (Join-Path $mainBin "DonJCustomNpcPlacer.dll"),
    (Join-Path $mainBin "DonJCustomNpcPlacer.ENdll"),
    (Join-Path $mainBin "DonJCustomNpcPlacer.pdb"),
    (Join-Path $packageRoot "DonJCustomNpcPlacer.ENdll"),
    (Join-Path $packageRoot "DonJCustomNpcPlacer.pdb"),
    (Join-Path $packageRoot "INSTALLATION_SIMPLE.txt"),
    (Join-Path $packageRoot "manifest.json")
)
if ($packageIsPublishable) {
    $expectedFiles += @(
        (Join-Path $deployRoot "DonJCustomNpcPlacer.ENdll"),
        (Join-Path $deployRoot "DonJCustomNpcPlacer.pdb"),
        (Join-Path $deployRoot "DonJCustomNpcPlacer.manifest.json")
    )
}

foreach ($file in $expectedFiles) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Fichier attendu introuvable apres validation: $file"
    }
}

$buildEndllHash = (Get-FileHash -LiteralPath (Join-Path $mainBin "DonJCustomNpcPlacer.ENdll") -Algorithm SHA256).Hash
$packageEndllHash = (Get-FileHash -LiteralPath (Join-Path $packageRoot "DonJCustomNpcPlacer.ENdll") -Algorithm SHA256).Hash
$buildPdbHash = (Get-FileHash -LiteralPath (Join-Path $mainBin "DonJCustomNpcPlacer.pdb") -Algorithm SHA256).Hash
$packagePdbHash = (Get-FileHash -LiteralPath (Join-Path $packageRoot "DonJCustomNpcPlacer.pdb") -Algorithm SHA256).Hash
$packageManifestHash = (Get-FileHash -LiteralPath (Join-Path $packageRoot "manifest.json") -Algorithm SHA256).Hash

if ($buildEndllHash -ne $packageEndllHash) {
    throw "Les SHA-256 build/package ENdll ne correspondent pas."
}
if ($buildPdbHash -ne $packagePdbHash) {
    throw "Les SHA-256 build/package PDB ne correspondent pas."
}
if ($packageIsPublishable) {
    $deployedEndllHash = (Get-FileHash -LiteralPath (Join-Path $deployRoot "DonJCustomNpcPlacer.ENdll") -Algorithm SHA256).Hash
    $deployedPdbHash = (Get-FileHash -LiteralPath (Join-Path $deployRoot "DonJCustomNpcPlacer.pdb") -Algorithm SHA256).Hash
    $deployedManifestHash = (Get-FileHash -LiteralPath (Join-Path $deployRoot "DonJCustomNpcPlacer.manifest.json") -Algorithm SHA256).Hash
    if ($packageEndllHash -ne $deployedEndllHash -or $packagePdbHash -ne $deployedPdbHash) {
        throw "Les SHA-256 package/déploiement ne correspondent pas."
    }
    if ($packageManifestHash -ne $deployedManifestHash) {
        throw "Le manifest déployé ne correspond pas au manifest canonique du package."
    }
}

$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName(
    (Join-Path $packageRoot "DonJCustomNpcPlacer.ENdll")).Version.ToString()
$informationalVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
    (Join-Path $packageRoot "DonJCustomNpcPlacer.ENdll")).ProductVersion

if ($manifest.product -ne "DonJCustomNpcPlacer" -or
    [int]$manifest.manifestVersion -ne 1 -or
    [long]$manifest.files.binary.sizeBytes -le 0 -or
    ([string]$manifest.files.binary.sha256).ToUpperInvariant() -ne $packageEndllHash.ToUpperInvariant() -or
    ([string]$manifest.files.symbols.sha256).ToUpperInvariant() -ne $packagePdbHash.ToUpperInvariant() -or
    [string]$manifest.assemblyVersion -ne $assemblyVersion -or
    [string]$manifest.informationalVersion -ne $informationalVersion -or
    [int]$manifest.justiceSchemaVersion -ne 2 -or
    $informationalVersion.IndexOf([string]$manifest.commit, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw "Le manifest du package ne correspond pas au binaire teste."
}

$requiredJusticeTypes = @(
    "DonJEnemySpawner",
    "JusticePolicy",
    "JusticeCaseState",
    "JusticePlayerProfileState",
    "JusticeTransition"
)
foreach ($typeName in $requiredJusticeTypes) {
    if (@($manifest.expectedTypes) -notcontains $typeName) {
        throw "Type Justice absent du contrat package: $typeName"
    }
}

if ($packageIsPublishable) {
    foreach ($fileName in $forbiddenDeployedFiles) {
        $candidate = Join-Path $deployRoot $fileName
        if (Test-Path -LiteralPath $candidate) {
            throw "Ancien fichier interdit encore présent dans le dossier de déploiement temporaire: $candidate"
        }
    }
}

$summaryPath = Join-Path $runRoot "summary.txt"
@(
    "Statut: OK",
    "CI: $Ci",
    "Stub API: $UseStubApi",
    "Dossier resultats: $runRoot",
    "Package game-ready: $packageRoot",
    "Dossier deploiement temporaire: $deployRoot",
    "SHA-256 ENdll: $packageEndllHash",
    "SHA-256 manifest: $packageManifestHash",
    "Package publiable: $packageIsPublishable",
    "Verification: restore + build sans déploiement + tests + package + contrat strict de déploiement ENdll/PDB/manifest"
) | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host "Suite securite OK. Resultats: $runRoot"

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$RepositoryRoot,

    [string]$BuildDirectory,

    [string]$OutputDirectory,

    [string]$DependencyDirectory,

    [string]$Commit,

    [switch]$AllowDirtySource,

    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-NormalizedFullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string]$BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    if ([string]::IsNullOrWhiteSpace($BasePath)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return [System.BitConverter]::ToString(
            $algorithm.ComputeHash($stream)).Replace("-", [string]::Empty)
    }
    finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

function Get-JusticeSchemaVersion {
    param(
        [Parameter(Mandatory = $true)]
        [System.Reflection.Assembly]$Assembly
    )

    $schemaBindingFlags = [System.Reflection.BindingFlags]"Public,NonPublic,Static"
    $codecType = $Assembly.GetType("JusticeXmlPersistenceCodec", $false)
    if ($null -ne $codecType) {
        $schemaField = $codecType.GetField("SchemaMajor", $schemaBindingFlags)
        if ($null -ne $schemaField -and $schemaField.IsLiteral) {
            return [int]$schemaField.GetRawConstantValue()
        }
    }

    # Je garde le fallback v1 pour pouvoir identifier explicitement un ancien build.
    $runtimeType = $Assembly.GetType("DonJEnemySpawner", $true)
    $legacySchemaField = $runtimeType.GetField("JusticeStateVersion", $schemaBindingFlags)
    if ($null -eq $legacySchemaField -or -not $legacySchemaField.IsLiteral) {
        throw "Le binaire ne publie aucune version de schema Justice detectable."
    }

    return [int]$legacySchemaField.GetRawConstantValue()
}

function Get-ScriptApiReferenceMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [System.Reflection.Assembly]$Assembly
    )

    $references = @(
        $Assembly.GetReferencedAssemblies() |
            Where-Object {
                $_.Name -eq "NIBScriptHookVDotNet2" -or
                $_.Name -eq "ScriptHookVDotNet2"
            }
    )
    if ($references.Count -ne 1) {
        throw "Le binaire doit referencer exactement une API ScriptHookVDotNet v2."
    }

    $reference = $references[0]
    if ($null -eq $reference.Version -or $reference.Version.Major -ne 2) {
        $detectedVersion = if ($null -eq $reference.Version) {
            "inconnue"
        }
        else {
            $reference.Version.ToString()
        }
        throw "Reference ScriptHookVDotNet incompatible: version majeure 2 attendue, $detectedVersion detectee."
    }

    return [pscustomobject]@{
        Name = $reference.Name
        Version = $reference.Version.ToString()
        Major = $reference.Version.Major
    }
}

function Get-JusticeAssemblyMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BinaryPath,

        [string[]]$DependencyDirectories
    )

    $resolvedDirectories = @(
        $DependencyDirectories |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { Get-NormalizedFullPath -Path $_ } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Container } |
            Select-Object -Unique
    )

    $expectedTypes = @(
        "DonJEnemySpawner",
        "JusticePolicy",
        "JusticeCaseState",
        "JusticePlayerProfileState",
        "JusticeTransition",
        "JusticeRepository",
        "JusticeWriteAheadLog",
        "JusticeXmlPersistenceCodec",
        "JusticeWorldSnapshot"
    )

    if ($PSVersionTable.PSEdition -eq "Desktop") {
        $resolveHandler = [System.ResolveEventHandler]{
            param($sender, $eventArgs)

            $requestedName = New-Object System.Reflection.AssemblyName($eventArgs.Name)
            foreach ($directory in $resolvedDirectories) {
                $candidate = Join-Path $directory ($requestedName.Name + ".dll")
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    return [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($candidate)
                }
            }

            return [System.Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name)
        }

        [System.AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($resolveHandler)
        try {
            $assembly = [System.Reflection.Assembly]::ReflectionOnlyLoad(
                [System.IO.File]::ReadAllBytes($BinaryPath))
            [void]$assembly.GetType("DonJEnemySpawner", $true)
            $justiceSchemaVersion = Get-JusticeSchemaVersion -Assembly $assembly
            $scriptApiReference = Get-ScriptApiReferenceMetadata -Assembly $assembly

            foreach ($typeName in $expectedTypes) {
                if ($null -eq $assembly.GetType($typeName, $false)) {
                    throw "Type Justice attendu absent du binaire: $typeName"
                }
            }

            return [pscustomobject]@{
                AssemblyVersion = $assembly.GetName().Version.ToString()
                JusticeSchemaVersion = $justiceSchemaVersion
                ScriptApiName = $scriptApiReference.Name
                ScriptApiVersion = $scriptApiReference.Version
                ScriptApiMajor = $scriptApiReference.Major
                ExpectedTypes = $expectedTypes
            }
        }
        finally {
            [System.AppDomain]::CurrentDomain.remove_ReflectionOnlyAssemblyResolve($resolveHandler)
        }
    }

    $loadHandler = [System.ResolveEventHandler]{
        param($sender, $eventArgs)

        $requestedName = [System.Reflection.AssemblyName]::new($eventArgs.Name)
        foreach ($directory in $resolvedDirectories) {
            $candidate = Join-Path $directory ($requestedName.Name + ".dll")
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return [System.Reflection.Assembly]::LoadFrom($candidate)
            }
        }

        return $null
    }

    [System.AppDomain]::CurrentDomain.add_AssemblyResolve($loadHandler)
    try {
        $assembly = [System.Reflection.Assembly]::Load(
            [System.IO.File]::ReadAllBytes($BinaryPath))
        [void]$assembly.GetType("DonJEnemySpawner", $true)
        $justiceSchemaVersion = Get-JusticeSchemaVersion -Assembly $assembly
        $scriptApiReference = Get-ScriptApiReferenceMetadata -Assembly $assembly

        foreach ($typeName in $expectedTypes) {
            if ($null -eq $assembly.GetType($typeName, $false)) {
                throw "Type Justice attendu absent du binaire: $typeName"
            }
        }

        return [pscustomobject]@{
            AssemblyVersion = $assembly.GetName().Version.ToString()
            JusticeSchemaVersion = $justiceSchemaVersion
            ScriptApiName = $scriptApiReference.Name
            ScriptApiVersion = $scriptApiReference.Version
            ScriptApiMajor = $scriptApiReference.Major
            ExpectedTypes = $expectedTypes
        }
    }
    finally {
        [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($loadHandler)
    }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}

$repositoryFullPath = Get-NormalizedFullPath -Path $RepositoryRoot
if (-not (Test-Path -LiteralPath (Join-Path $repositoryFullPath "GTA5modDEV.sln") -PathType Leaf)) {
    throw "Racine du depot invalide: $repositoryFullPath"
}

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $repositoryFullPath "src\DonJEnemySpawner\bin\$Configuration"
}
$buildFullPath = Get-NormalizedFullPath -Path $BuildDirectory -BasePath $repositoryFullPath

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryFullPath "artifacts\game-ready"
}
$outputFullPath = Get-NormalizedFullPath -Path $OutputDirectory -BasePath $repositoryFullPath

$outputParent = Split-Path -Parent $outputFullPath
$outputLeaf = Split-Path -Leaf $outputFullPath
$driveRoot = [System.IO.Path]::GetPathRoot($outputFullPath).TrimEnd('\', '/')
$normalizedOutputWithoutSeparator = $outputFullPath.TrimEnd('\', '/')

if ([string]::IsNullOrWhiteSpace($outputLeaf) -or
    [string]::Equals($normalizedOutputWithoutSeparator, $driveRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($normalizedOutputWithoutSeparator, $repositoryFullPath.TrimEnd('\', '/'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Dossier de package trop large ou dangereux: $outputFullPath"
}

$buildEndll = Join-Path $buildFullPath "DonJCustomNpcPlacer.ENdll"
$buildPdb = Join-Path $buildFullPath "DonJCustomNpcPlacer.pdb"
$installationGuide = Join-Path $repositoryFullPath "Mode-pour-jeu-ici\INSTALLATION_SIMPLE.txt"

foreach ($requiredFile in @($buildEndll, $buildPdb, $installationGuide)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Fichier requis introuvable pour le package: $requiredFile"
    }
}

if ([string]::IsNullOrWhiteSpace($Commit)) {
    $commitOutput = @(& git -C $repositoryFullPath rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "Impossible de determiner le commit Git du package."
    }
    $Commit = ($commitOutput -join "").Trim()
}

if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Commit Git invalide pour le package: $Commit"
}
$Commit = $Commit.ToLowerInvariant()

$fileVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($buildEndll)
$informationalVersion = $fileVersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($informationalVersion) -or
    $informationalVersion.IndexOf($Commit, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw "La version informationnelle '$informationalVersion' ne contient pas le commit $Commit."
}

$dependencyDirectories = @($buildFullPath)
if (-not [string]::IsNullOrWhiteSpace($DependencyDirectory)) {
    $dependencyDirectories += $DependencyDirectory
}
$assemblyMetadata = Get-JusticeAssemblyMetadata `
    -BinaryPath $buildEndll `
    -DependencyDirectories $dependencyDirectories

if ($assemblyMetadata.JusticeSchemaVersion -ne 2) {
    throw "Version de schema Justice incompatible: 2 attendue, $($assemblyMetadata.JusticeSchemaVersion) detectee."
}

$sourceStatus = @(& git -C $repositoryFullPath status --porcelain 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw "Impossible de verifier l'etat Git de la source du package."
}

$sourceDirty = $sourceStatus.Count -gt 0
if ($sourceDirty -and -not $AllowDirtySource) {
    throw "La source Git contient des changements non valides. Utilise -AllowDirtySource uniquement pour un package local explicitement non publiable."
}

if (Test-Path -LiteralPath $outputFullPath) {
    if (-not (Test-Path -LiteralPath $outputFullPath -PathType Container)) {
        throw "La destination du package existe mais n'est pas un dossier: $outputFullPath"
    }

    if (-not $Force) {
        throw "Le dossier de package existe deja. Utilise -Force pour le remplacer: $outputFullPath"
    }

    # Je refuse que -Force transforme un dossier arbitraire en package de livraison.
    $existingEntries = @(Get-ChildItem -LiteralPath $outputFullPath -Force)
    if ($existingEntries.Count -gt 0) {
        $existingManifestPath = Join-Path $outputFullPath "manifest.json"
        try {
            $existingManifest = Get-Content -Raw -LiteralPath $existingManifestPath | ConvertFrom-Json
        }
        catch {
            throw "Le dossier existant n'est pas un package game-ready reconnu: $outputFullPath"
        }

        if ($null -eq $existingManifest -or
            [int]$existingManifest.manifestVersion -ne 1 -or
            [string]$existingManifest.product -ne "DonJCustomNpcPlacer") {
            throw "Le dossier existant n'est pas un package game-ready reconnu: $outputFullPath"
        }
    }
}

New-Item -ItemType Directory -Force -Path $outputParent | Out-Null
$transactionId = [System.Guid]::NewGuid().ToString("N")
$stagingDirectory = Join-Path $outputParent ("." + $outputLeaf + ".staging-" + $transactionId)
$previousDirectory = Join-Path $outputParent ("." + $outputLeaf + ".previous-" + $transactionId)
$previousMoved = $false

try {
    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null

    $packageEndll = Join-Path $stagingDirectory "DonJCustomNpcPlacer.ENdll"
    $packagePdb = Join-Path $stagingDirectory "DonJCustomNpcPlacer.pdb"
    $packageGuide = Join-Path $stagingDirectory "INSTALLATION_SIMPLE.txt"

    Copy-Item -LiteralPath $buildEndll -Destination $packageEndll
    Copy-Item -LiteralPath $buildPdb -Destination $packagePdb
    Copy-Item -LiteralPath $installationGuide -Destination $packageGuide

    $buildEndllHash = Get-Sha256 -Path $buildEndll
    $packageEndllHash = Get-Sha256 -Path $packageEndll
    $buildPdbHash = Get-Sha256 -Path $buildPdb
    $packagePdbHash = Get-Sha256 -Path $packagePdb

    if ($buildEndllHash -ne $packageEndllHash) {
        throw "Le binaire package ne correspond pas au binaire compile et teste."
    }
    if ($buildPdbHash -ne $packagePdbHash) {
        throw "Le PDB package ne correspond pas au PDB du meme build."
    }

    $manifest = [ordered]@{
        manifestVersion = 1
        product = "DonJCustomNpcPlacer"
        configuration = $Configuration
        generatedAtUtc = [System.DateTime]::UtcNow.ToString("O")
        commit = $Commit
        sourceDirty = $sourceDirty
        assemblyVersion = $assemblyMetadata.AssemblyVersion
        informationalVersion = $informationalVersion
        justiceSchemaVersion = $assemblyMetadata.JusticeSchemaVersion
        scriptApi = [ordered]@{
            name = $assemblyMetadata.ScriptApiName
            version = $assemblyMetadata.ScriptApiVersion
            major = $assemblyMetadata.ScriptApiMajor
        }
        expectedTypes = $assemblyMetadata.ExpectedTypes
        files = [ordered]@{
            binary = [ordered]@{
                name = "DonJCustomNpcPlacer.ENdll"
                sizeBytes = (Get-Item -LiteralPath $packageEndll).Length
                sha256 = $packageEndllHash
            }
            symbols = [ordered]@{
                name = "DonJCustomNpcPlacer.pdb"
                sizeBytes = (Get-Item -LiteralPath $packagePdb).Length
                sha256 = $packagePdbHash
            }
            installationGuide = [ordered]@{
                name = "INSTALLATION_SIMPLE.txt"
                sizeBytes = (Get-Item -LiteralPath $packageGuide).Length
                sha256 = Get-Sha256 -Path $packageGuide
            }
        }
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText(
        (Join-Path $stagingDirectory "manifest.json"),
        $manifestJson,
        (New-Object System.Text.UTF8Encoding($false)))

    if (Test-Path -LiteralPath $outputFullPath) {
        [System.IO.Directory]::Move($outputFullPath, $previousDirectory)
        $previousMoved = $true
    }

    try {
        [System.IO.Directory]::Move($stagingDirectory, $outputFullPath)

        if ((Get-Sha256 -Path (Join-Path $outputFullPath "DonJCustomNpcPlacer.ENdll")) -ne $buildEndllHash -or
            (Get-Sha256 -Path (Join-Path $outputFullPath "DonJCustomNpcPlacer.pdb")) -ne $buildPdbHash) {
            throw "Le package final ne correspond plus aux fichiers de build apres publication locale."
        }
    }
    catch {
        $publicationFailure = $_

        # Je retire la nouvelle publication par renommage avant de restaurer l'ancienne.
        if (Test-Path -LiteralPath $outputFullPath -PathType Container) {
            [System.IO.Directory]::Move($outputFullPath, $stagingDirectory)
        }
        if ($previousMoved -and -not (Test-Path -LiteralPath $outputFullPath)) {
            [System.IO.Directory]::Move($previousDirectory, $outputFullPath)
            $previousMoved = $false
        }

        throw $publicationFailure
    }

    if ($previousMoved -and (Test-Path -LiteralPath $previousDirectory)) {
        Remove-Item -LiteralPath $previousDirectory -Recurse -Force
        $previousMoved = $false
    }

    Write-Host "Package game-ready verifie: $outputFullPath"
    Write-Host "SHA-256 ENdll: $buildEndllHash"
    Write-Host "Commit: $Commit"
    Write-Host "Schema Justice: $($assemblyMetadata.JusticeSchemaVersion)"
    Write-Host "API ScriptHookVDotNet: $($assemblyMetadata.ScriptApiName) $($assemblyMetadata.ScriptApiVersion)"
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }

    if ($previousMoved -and
        (Test-Path -LiteralPath $previousDirectory) -and
        -not (Test-Path -LiteralPath $outputFullPath)) {
        [System.IO.Directory]::Move($previousDirectory, $outputFullPath)
    }
}

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$RepositoryRoot,

    [string]$BuildDirectory,

    [string]$OutputDirectory,

    [string]$DependencyDirectory,

    [string]$AbiValidatorPath,

    [string]$AbiContractPath,

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

function Invoke-AbiValidator {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ValidatorPath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    $output = @(& $ValidatorPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $details = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "La verification ABI '$Operation' a echoue avec le code $exitCode.$([Environment]::NewLine)$details"
    }

    return $output
}

function Get-AbiContractMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ValidatorPath,

        [Parameter(Mandatory = $true)]
        [string]$ContractPath
    )

    $output = Invoke-AbiValidator `
        -ValidatorPath $ValidatorPath `
        -Arguments @("info", "--contract", $ContractPath) `
        -Operation "lecture du contrat"
    $json = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    try {
        $metadata = $json | ConvertFrom-Json
    }
    catch {
        throw "Le validateur ABI a retourne des metadonnees illisibles: $json"
    }

    $actualHash = Get-Sha256 -Path $ContractPath
    if ($null -eq $metadata -or
        [string]::IsNullOrWhiteSpace([string]$metadata.contractId) -or
        [string]::IsNullOrWhiteSpace([string]$metadata.contractVersion) -or
        ([string]$metadata.sha256) -notmatch '^[0-9a-fA-F]{64}$' -or
        -not [string]::Equals(
            ([string]$metadata.sha256).ToUpperInvariant(),
            $actualHash,
            [System.StringComparison]::Ordinal)) {
        throw "Les metadonnees du contrat ABI ne correspondent pas a son contenu: $ContractPath"
    }

    return [pscustomobject]@{
        Id = [string]$metadata.contractId
        Version = [string]$metadata.contractVersion
        Sha256 = $actualHash
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

function Get-HudRendererProviderMetadata {
    param(
        [string]$Directory
    )

    if ([string]::IsNullOrWhiteSpace($Directory)) {
        return $null
    }

    $directoryFullPath = Get-NormalizedFullPath -Path $Directory
    $runtimePath = Join-Path $directoryFullPath "NIBScriptHookVDotNet3.dll"
    if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf)) {
        return $null
    }

    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($runtimePath)
    if ($assemblyName.Name -ne "NIBScriptHookVDotNet3" -or
        $null -eq $assemblyName.Version -or
        $assemblyName.Version.Major -lt 3) {
        throw "Provider HUD present mais incompatible: NIBScriptHookVDotNet3 majeure 3 ou superieure attendue."
    }

    if ($PSVersionTable.PSEdition -eq "Desktop") {
        # Je précharge les types graphiques avant l'inspection reflection-only du
        # provider optionnel réellement présent dans les dépendances du package.
        [void][System.Reflection.Assembly]::ReflectionOnlyLoad(
            "System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")
        $assembly = [System.Reflection.Assembly]::ReflectionOnlyLoad(
            [System.IO.File]::ReadAllBytes($runtimePath))
    }
    else {
        $assembly = [System.Reflection.Assembly]::Load(
            [System.IO.File]::ReadAllBytes($runtimePath))
    }

    $spriteType = $assembly.GetType("GTA.UI.CustomSprite", $false)
    if ($null -eq $spriteType) {
        throw "Provider HUD present mais type GTA.UI.CustomSprite absent."
    }

    $positionProperty = $spriteType.GetProperty(
        "Position",
        [System.Reflection.BindingFlags]"Public,Instance")
    $sizeProperty = $spriteType.GetProperty(
        "Size",
        [System.Reflection.BindingFlags]"Public,Instance")
    $colorProperty = $spriteType.GetProperty(
        "Color",
        [System.Reflection.BindingFlags]"Public,Instance")
    $centeredProperty = $spriteType.GetProperty(
        "Centered",
        [System.Reflection.BindingFlags]"Public,Instance")
    if ($null -eq $positionProperty -or
        -not $positionProperty.CanWrite -or
        $positionProperty.PropertyType.FullName -ne "System.Drawing.PointF" -or
        $null -eq $sizeProperty -or
        -not $sizeProperty.CanWrite -or
        $sizeProperty.PropertyType.FullName -ne "System.Drawing.SizeF" -or
        $null -eq $colorProperty -or
        -not $colorProperty.CanWrite -or
        $colorProperty.PropertyType.FullName -ne "System.Drawing.Color" -or
        $null -eq $centeredProperty -or
        -not $centeredProperty.CanWrite -or
        $centeredProperty.PropertyType.FullName -ne "System.Boolean") {
        throw "Provider HUD present mais propriétés CustomSprite incompatibles."
    }

    $compatibleConstructor = $false
    foreach ($constructor in $spriteType.GetConstructors(
        [System.Reflection.BindingFlags]"Public,NonPublic,Instance")) {
        $parameters = @($constructor.GetParameters())
        if ($parameters.Count -eq 3 -and
            $parameters[0].ParameterType.FullName -eq "System.String" -and
            $parameters[1].ParameterType.FullName -eq "System.Drawing.SizeF" -and
            $parameters[2].ParameterType.FullName -eq "System.Drawing.PointF") {
            $compatibleConstructor = $true
            break
        }
    }

    $compatibleDraw = $false
    foreach ($method in $spriteType.GetMethods(
        [System.Reflection.BindingFlags]"Public,Instance")) {
        if ($method.Name -eq "Draw" -and @($method.GetParameters()).Count -eq 0) {
            $compatibleDraw = $true
            break
        }
    }

    if (-not $compatibleConstructor -or -not $compatibleDraw) {
        throw "Provider HUD present mais CustomSprite doit fournir .ctor(string, SizeF, PointF) et Draw()."
    }

    return [pscustomobject]@{
        AssemblyName = $assemblyName.Name
        Version = $assemblyName.Version.ToString()
        Major = $assemblyName.Version.Major
        TypeName = $spriteType.FullName
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
        "JusticeWorldSnapshot",
        "DonJ.JusticeRecognition.DonJJusticeRecognitionScript"
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

$scriptRepositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AbiValidatorPath)) {
    $AbiValidatorPath = Join-Path $scriptRepositoryRoot "tools\NibAbiValidator\bin\Release\DonJ.NibAbiValidator.exe"
}
if ([string]::IsNullOrWhiteSpace($AbiContractPath)) {
    $AbiContractPath = Join-Path $scriptRepositoryRoot "tools\NibAbiValidator\contracts\NIBScriptHookVDotNet2-2.11.6.abi.xml"
}
$abiValidatorFullPath = Get-NormalizedFullPath -Path $AbiValidatorPath -BasePath $repositoryFullPath
$abiContractFullPath = Get-NormalizedFullPath -Path $AbiContractPath -BasePath $repositoryFullPath
if (-not (Test-Path -LiteralPath $abiValidatorFullPath -PathType Leaf)) {
    throw "Validateur ABI introuvable: $abiValidatorFullPath"
}
if (-not (Test-Path -LiteralPath $abiContractFullPath -PathType Leaf)) {
    throw "Contrat ABI introuvable: $abiContractFullPath"
}
$abiContract = Get-AbiContractMetadata `
    -ValidatorPath $abiValidatorFullPath `
    -ContractPath $abiContractFullPath

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

$justiceAssetDefinitions = @(
    [pscustomobject]@{
        Key = "immatriculation"
        RelativeName = "Assets/Justice/immatriculation.png"
    },
    [pscustomobject]@{
        Key = "outfit"
        RelativeName = "Assets/Justice/tenue.png"
    },
    [pscustomobject]@{
        Key = "warrant"
        RelativeName = "Assets/Justice/mandat.png"
    }
)

$justiceAssets = @(
    foreach ($definition in $justiceAssetDefinitions) {
        $sourcePath = Join-Path `
            (Join-Path $repositoryFullPath "src\DonJEnemySpawner") `
            $definition.RelativeName
        $buildPath = Join-Path $buildFullPath $definition.RelativeName

        foreach ($requiredAsset in @($sourcePath, $buildPath)) {
            if (-not (Test-Path -LiteralPath $requiredAsset -PathType Leaf) -or
                (Get-Item -LiteralPath $requiredAsset).Length -le 0) {
                throw "Asset Justice requis introuvable ou vide: $requiredAsset"
            }
        }

        $sourceHash = Get-Sha256 -Path $sourcePath
        $buildHash = Get-Sha256 -Path $buildPath
        if ($sourceHash -ne $buildHash) {
            throw "L'asset Justice de build ne correspond pas a la source: $($definition.RelativeName)"
        }

        [pscustomobject]@{
            Key = $definition.Key
            RelativeName = $definition.RelativeName
            SourcePath = $sourcePath
            BuildPath = $buildPath
            SourceHash = $sourceHash
            BuildHash = $buildHash
            Length = (Get-Item -LiteralPath $buildPath).Length
        }
    }
)

foreach ($requiredFile in @($buildEndll, $buildPdb, $installationGuide)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Fichier requis introuvable pour le package: $requiredFile"
    }
}

$validatedBuildEndllHash = Get-Sha256 -Path $buildEndll
$validatedBuildPdbHash = Get-Sha256 -Path $buildPdb
[void](Invoke-AbiValidator `
    -ValidatorPath $abiValidatorFullPath `
    -Arguments @(
        "verify",
        "--consumer", $buildEndll,
        "--contract", $abiContractFullPath) `
    -Operation "compatibilite du binaire avec le contrat canonique")

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
$hudRendererProvider = Get-HudRendererProviderMetadata `
    -Directory $DependencyDirectory

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

        $existingManifestVersion = if ($null -eq $existingManifest) {
            0
        }
        else {
            [int]$existingManifest.manifestVersion
        }
        if ($null -eq $existingManifest -or
            ($existingManifestVersion -ne 1 -and $existingManifestVersion -ne 2) -or
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
    $packageJusticeDirectory = Join-Path $stagingDirectory "Assets\Justice"

    New-Item -ItemType Directory -Path $packageJusticeDirectory | Out-Null
    Copy-Item -LiteralPath $buildEndll -Destination $packageEndll
    Copy-Item -LiteralPath $buildPdb -Destination $packagePdb
    Copy-Item -LiteralPath $installationGuide -Destination $packageGuide

    $packagedJusticeAssetEntries = [ordered]@{}
    foreach ($justiceAsset in $justiceAssets) {
        $packageAssetPath = Join-Path $stagingDirectory $justiceAsset.RelativeName
        Copy-Item -LiteralPath $justiceAsset.BuildPath -Destination $packageAssetPath

        $packageAssetHash = Get-Sha256 -Path $packageAssetPath
        $packageAssetLength = (Get-Item -LiteralPath $packageAssetPath).Length
        if ($packageAssetHash -ne $justiceAsset.SourceHash -or
            $packageAssetHash -ne $justiceAsset.BuildHash -or
            $packageAssetLength -ne $justiceAsset.Length) {
            throw "L'asset Justice package ne correspond pas a la source et au build: $($justiceAsset.RelativeName)"
        }

        $packagedJusticeAssetEntries[$justiceAsset.Key] = [ordered]@{
            name = $justiceAsset.RelativeName
            sizeBytes = $packageAssetLength
            sha256 = $packageAssetHash
        }
    }

    $buildEndllHash = Get-Sha256 -Path $buildEndll
    $packageEndllHash = Get-Sha256 -Path $packageEndll
    $buildPdbHash = Get-Sha256 -Path $buildPdb
    $packagePdbHash = Get-Sha256 -Path $packagePdb

    if ($buildEndllHash -ne $validatedBuildEndllHash -or
        $packageEndllHash -ne $validatedBuildEndllHash) {
        throw "Le binaire a change apres sa validation ABI ou pendant sa copie vers le package."
    }
    if ($buildPdbHash -ne $validatedBuildPdbHash -or
        $packagePdbHash -ne $validatedBuildPdbHash) {
        throw "Le PDB a change pendant sa copie vers le package."
    }

    # Je revalide les octets exacts qui seront publies afin qu'une reecriture
    # concurrente du dossier de build ne puisse jamais contourner le contrat ABI.
    [void](Invoke-AbiValidator `
        -ValidatorPath $abiValidatorFullPath `
        -Arguments @(
            "verify",
            "--consumer", $packageEndll,
            "--contract", $abiContractFullPath) `
        -Operation "compatibilite de la copie package avec le contrat canonique")

    $manifest = [ordered]@{
        manifestVersion = 2
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
            abiContract = [ordered]@{
                id = $abiContract.Id
                version = $abiContract.Version
                sha256 = $abiContract.Sha256
            }
        }
        hudRenderer = [ordered]@{
            optional = $true
            fallback = "native"
            available = $null -ne $hudRendererProvider
            assemblyName = if ($null -eq $hudRendererProvider) { $null } else { $hudRendererProvider.AssemblyName }
            version = if ($null -eq $hudRendererProvider) { $null } else { $hudRendererProvider.Version }
            minimumMajor = 3
            typeName = "GTA.UI.CustomSprite"
            contractVersion = 1
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
            justiceAssets = $packagedJusticeAssetEntries
        }
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 8
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

        foreach ($justiceAsset in $justiceAssets) {
            $finalAssetPath = Join-Path $outputFullPath $justiceAsset.RelativeName
            if (-not (Test-Path -LiteralPath $finalAssetPath -PathType Leaf) -or
                (Get-Sha256 -Path $finalAssetPath) -ne $justiceAsset.SourceHash -or
                (Get-Item -LiteralPath $finalAssetPath).Length -ne $justiceAsset.Length) {
                throw "L'asset Justice final ne correspond plus a la source validee: $($justiceAsset.RelativeName)"
            }
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
    Write-Host "Contrat ABI: $($abiContract.Id) $($abiContract.Version) $($abiContract.Sha256)"
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

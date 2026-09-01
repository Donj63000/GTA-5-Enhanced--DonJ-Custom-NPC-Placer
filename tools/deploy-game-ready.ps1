[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$GtaRoot,

    [string]$GtaScriptsDir,

    [string]$AbiValidatorPath,

    [string]$AbiContractPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-NormalizedFullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
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

function Get-ScriptApiReferenceMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BinaryPath
    )

    if ($PSVersionTable.PSEdition -eq "Desktop") {
        $assembly = [System.Reflection.Assembly]::ReflectionOnlyLoad(
            [System.IO.File]::ReadAllBytes($BinaryPath))
    }
    else {
        $assembly = [System.Reflection.Assembly]::Load(
            [System.IO.File]::ReadAllBytes($BinaryPath))
    }

    $references = @(
        $assembly.GetReferencedAssemblies() |
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

function Assert-ManifestFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [object]$Entry,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedName
    )

    if ($null -eq $Entry -or $Entry.name -ne $ExpectedName) {
        throw "Entree de manifest invalide pour $ExpectedName."
    }

    $path = Join-Path $Directory $ExpectedName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Fichier du package introuvable: $path"
    }

    $item = Get-Item -LiteralPath $path
    if ($item.Length -le 0 -or $item.Length -ne [long]$Entry.sizeBytes) {
        throw "Taille invalide pour $ExpectedName."
    }

    $hash = Get-Sha256 -Path $path
    if ($hash -ne ([string]$Entry.sha256).ToUpperInvariant()) {
        throw "SHA-256 invalide pour $ExpectedName."
    }

    return [pscustomobject]@{
        Path = $path
        Hash = $hash
        Length = $item.Length
    }
}

function Assert-HudRendererRuntime {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GtaDirectory,

        [Parameter(Mandatory = $true)]
        [object]$Descriptor
    )

    if ($null -eq $Descriptor -or
        [string]$Descriptor.assemblyName -ne "NIBScriptHookVDotNet3" -or
        [int]$Descriptor.minimumMajor -ne 3 -or
        [string]$Descriptor.typeName -ne "GTA.UI.CustomSprite" -or
        [int]$Descriptor.contractVersion -ne 1) {
        throw "Contrat HUD du manifest invalide."
    }

    $runtimePath = Join-Path $GtaDirectory ([string]$Descriptor.assemblyName + ".dll")
    if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf)) {
        throw "Renderer HUD NIB v3 introuvable avant deploiement: $runtimePath"
    }

    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($runtimePath)
    if ($assemblyName.Name -ne [string]$Descriptor.assemblyName -or
        $null -eq $assemblyName.Version -or
        $assemblyName.Version.Major -lt [int]$Descriptor.minimumMajor) {
        throw "Renderer HUD incompatible: NIBScriptHookVDotNet3 version majeure 3 ou superieure attendue."
    }

    if ($PSVersionTable.PSEdition -eq "Desktop") {
        # Je précharge la dépendance des propriétés PointF/SizeF avant d'inspecter
        # le type en reflection-only sous Windows PowerShell.
        [void][System.Reflection.Assembly]::ReflectionOnlyLoad(
            "System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")
        $assembly = [System.Reflection.Assembly]::ReflectionOnlyLoad(
            [System.IO.File]::ReadAllBytes($runtimePath))
    }
    else {
        $assembly = [System.Reflection.Assembly]::Load(
            [System.IO.File]::ReadAllBytes($runtimePath))
    }

    $spriteType = $assembly.GetType([string]$Descriptor.typeName, $false)
    if ($null -eq $spriteType) {
        throw "Type HUD attendu absent du renderer NIB v3: $($Descriptor.typeName)"
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
        throw "GTA.UI.CustomSprite ne publie pas les proprietes PointF/SizeF/Color/Centered compatibles."
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
        throw "GTA.UI.CustomSprite doit fournir .ctor(string, SizeF, PointF) et Draw()."
    }

    return [pscustomobject]@{
        Path = $runtimePath
        Name = $assemblyName.Name
        Version = $assemblyName.Version.ToString()
        Major = $assemblyName.Version.Major
        TypeName = $spriteType.FullName
    }
}

function Backup-ExistingTargetFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [Parameter(Mandatory = $true)]
        [string]$BackupPath
    )

    if (-not (Test-Path -LiteralPath $TargetPath -PathType Leaf)) {
        return $false
    }

    # Je crée le backup avant l'appel de remplacement. Ainsi, même si l'API
    # système remplace les octets puis lève une exception, le rollback possède
    # déjà une copie indépendante de l'ancienne version.
    Copy-Item -LiteralPath $TargetPath -Destination $BackupPath
    return $true
}

function Install-StagedFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StagedPath,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [Parameter(Mandatory = $true)]
        [string]$BackupPath,

        [Parameter(Mandatory = $true)]
        [bool]$HadOriginal
    )

    if ($HadOriginal) {
        if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) {
            throw "Backup préparatoire absent avant remplacement: $BackupPath"
        }

        $replacementDiscardPath = $BackupPath + ".replace-discard"
        try {
            [System.IO.File]::Replace(
                $StagedPath,
                $TargetPath,
                $replacementDiscardPath,
                $true)
        }
        finally {
            if (Test-Path -LiteralPath $replacementDiscardPath -PathType Leaf) {
                Remove-Item -LiteralPath $replacementDiscardPath -Force
            }
        }
        return
    }

    [System.IO.File]::Move($StagedPath, $TargetPath)
}

function Restore-PreviousFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [Parameter(Mandatory = $true)]
        [string]$BackupPath,

        [Parameter(Mandatory = $true)]
        [bool]$HadOriginal
    )

    if (-not $HadOriginal) {
        if (Test-Path -LiteralPath $TargetPath -PathType Leaf) {
            Remove-Item -LiteralPath $TargetPath -Force
        }
        return
    }

    if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) {
        throw "Rollback impossible, backup absent: $BackupPath"
    }

    if (Test-Path -LiteralPath $TargetPath -PathType Leaf) {
        $discardPath = $BackupPath + ".rollback-discard"
        [System.IO.File]::Replace($BackupPath, $TargetPath, $discardPath, $true)
        if (Test-Path -LiteralPath $discardPath) {
            Remove-Item -LiteralPath $discardPath -Force
        }
        return
    }

    [System.IO.File]::Move($BackupPath, $TargetPath)
}

function Assert-GameScriptHostsStopped {
    $protectedProcessNames = @(
        "GTA5_Enhanced",
        "GTA5",
        "PlayGTAV"
    )
    $runningProcesses = @(
        Get-Process `
            -Name $protectedProcessNames `
            -ErrorAction SilentlyContinue |
            Sort-Object -Property Id -Unique
    )

    if ($runningProcesses.Count -eq 0) {
        return
    }

    $details = @(
        $runningProcesses |
            ForEach-Object {
                $_.ProcessName + " (PID " +
                $_.Id.ToString([System.Globalization.CultureInfo]::InvariantCulture) +
                ")"
            }
    ) -join ", "
    throw "Deploiement refuse: ferme GTA et ses hosts de scripts avant toute modification de Scripts. Processus detectes: $details"
}

$packageFullPath = Get-NormalizedFullPath -Path $PackageDirectory
$gtaRootFullPath = Get-NormalizedFullPath -Path $GtaRoot
$repositoryFullPath = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AbiValidatorPath)) {
    $AbiValidatorPath = Join-Path $repositoryFullPath "tools\NibAbiValidator\bin\Release\DonJ.NibAbiValidator.exe"
}
if ([string]::IsNullOrWhiteSpace($AbiContractPath)) {
    $AbiContractPath = Join-Path $repositoryFullPath "tools\NibAbiValidator\contracts\NIBScriptHookVDotNet2-2.11.6.abi.xml"
}
$abiValidatorFullPath = Get-NormalizedFullPath -Path $AbiValidatorPath
$abiContractFullPath = Get-NormalizedFullPath -Path $AbiContractPath
if (-not (Test-Path -LiteralPath $abiValidatorFullPath -PathType Leaf)) {
    throw "Validateur ABI introuvable: $abiValidatorFullPath"
}
if (-not (Test-Path -LiteralPath $abiContractFullPath -PathType Leaf)) {
    throw "Contrat ABI introuvable: $abiContractFullPath"
}
$canonicalAbiContract = Get-AbiContractMetadata `
    -ValidatorPath $abiValidatorFullPath `
    -ContractPath $abiContractFullPath
if ([string]::IsNullOrWhiteSpace($GtaScriptsDir)) {
    $GtaScriptsDir = Join-Path $gtaRootFullPath "Scripts"
}
$scriptsFullPath = Get-NormalizedFullPath -Path $GtaScriptsDir

if (-not (Test-Path -LiteralPath $packageFullPath -PathType Container)) {
    throw "Package game-ready introuvable: $packageFullPath"
}
if (-not (Test-Path -LiteralPath (Join-Path $gtaRootFullPath "GTA5_Enhanced.exe") -PathType Leaf)) {
    throw "GTA5_Enhanced.exe introuvable dans le dossier GTA indique: $gtaRootFullPath"
}

$gtaPrefix = $gtaRootFullPath + [System.IO.Path]::DirectorySeparatorChar
if (-not $scriptsFullPath.StartsWith($gtaPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Le dossier Scripts doit rester sous le GtaRoot valide: $scriptsFullPath"
}

$manifestPath = Join-Path $packageFullPath "manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest du package introuvable: $manifestPath"
}
$manifestHash = Get-Sha256 -Path $manifestPath

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$scriptApiProperty = if ($null -eq $manifest) {
    $null
}
else {
    $manifest.PSObject.Properties["scriptApi"]
}
$scriptApi = if ($null -eq $scriptApiProperty) {
    $null
}
else {
    $scriptApiProperty.Value
}
$abiContractProperty = if ($null -eq $scriptApi) {
    $null
}
else {
    $scriptApi.PSObject.Properties["abiContract"]
}
$abiContract = if ($null -eq $abiContractProperty) {
    $null
}
else {
    $abiContractProperty.Value
}
$hudRendererProperty = if ($null -eq $manifest) {
    $null
}
else {
    $manifest.PSObject.Properties["hudRenderer"]
}
$hudRenderer = if ($null -eq $hudRendererProperty) {
    $null
}
else {
    $hudRendererProperty.Value
}
$hudRendererAvailable = $false
$hudRendererVersion = $null
if ($null -ne $hudRenderer) {
    $hudRendererAvailable = [bool]$hudRenderer.available
    if ($hudRendererAvailable) {
        try {
            $hudRendererVersion = [System.Version]([string]$hudRenderer.version)
        }
        catch {
            throw "Version du provider HUD illisible dans le manifest."
        }
    }
}
if ($null -eq $manifest -or
    [int]$manifest.manifestVersion -ne 2 -or
    $manifest.product -ne "DonJCustomNpcPlacer" -or
    ([string]$manifest.commit) -notmatch '^[0-9a-fA-F]{40}$' -or
    [int]$manifest.justiceSchemaVersion -ne 2 -or
    $null -eq $scriptApi -or
    [int]$scriptApi.major -ne 2 -or
    $null -eq $abiContract -or
    [string]::IsNullOrWhiteSpace([string]$abiContract.id) -or
    [string]::IsNullOrWhiteSpace([string]$abiContract.version) -or
    ([string]$abiContract.sha256) -notmatch '^[0-9a-fA-F]{64}$' -or
    $null -eq $hudRenderer -or
    -not [bool]$hudRenderer.optional -or
    [string]$hudRenderer.fallback -ne "native" -or
    [int]$hudRenderer.minimumMajor -ne 3 -or
    [string]$hudRenderer.typeName -ne "GTA.UI.CustomSprite" -or
    [int]$hudRenderer.contractVersion -ne 1 -or
    [bool]$manifest.sourceDirty) {
    throw "Manifest game-ready invalide: $manifestPath"
}
if ($hudRendererAvailable) {
    if ([string]$hudRenderer.assemblyName -ne "NIBScriptHookVDotNet3" -or
        $null -eq $hudRendererVersion -or
        $hudRendererVersion.Major -lt 3) {
        throw "Provider HUD déclaré incompatible dans le manifest."
    }
}
elseif (-not [string]::IsNullOrWhiteSpace([string]$hudRenderer.assemblyName) -or
        -not [string]::IsNullOrWhiteSpace([string]$hudRenderer.version)) {
    throw "Le manifest déclare un provider HUD absent avec une identité non vide."
}

$binary = Assert-ManifestFile `
    -Directory $packageFullPath `
    -Entry $manifest.files.binary `
    -ExpectedName "DonJCustomNpcPlacer.ENdll"
$symbols = Assert-ManifestFile `
    -Directory $packageFullPath `
    -Entry $manifest.files.symbols `
    -ExpectedName "DonJCustomNpcPlacer.pdb"
[void](Assert-ManifestFile `
    -Directory $packageFullPath `
    -Entry $manifest.files.installationGuide `
    -ExpectedName "INSTALLATION_SIMPLE.txt")

$justiceAssetEntriesProperty = $manifest.files.PSObject.Properties["justiceAssets"]
if ($null -eq $justiceAssetEntriesProperty -or
    $null -eq $justiceAssetEntriesProperty.Value) {
    throw "Catalogue des assets Justice absent du manifest."
}
$justiceAssetEntries = $justiceAssetEntriesProperty.Value
$justiceAssets = @(
    Assert-ManifestFile `
        -Directory $packageFullPath `
        -Entry $justiceAssetEntries.immatriculation `
        -ExpectedName "Assets/Justice/immatriculation.png"
    Assert-ManifestFile `
        -Directory $packageFullPath `
        -Entry $justiceAssetEntries.outfit `
        -ExpectedName "Assets/Justice/tenue.png"
    Assert-ManifestFile `
        -Directory $packageFullPath `
        -Entry $justiceAssetEntries.warrant `
        -ExpectedName "Assets/Justice/mandat.png"
)

$packageJusticeDirectory = Join-Path $packageFullPath "Assets\Justice"
$actualJusticeAssetNames = @(
    if (Test-Path -LiteralPath $packageJusticeDirectory -PathType Container) {
        Get-ChildItem -LiteralPath $packageJusticeDirectory -File -Recurse |
            ForEach-Object {
                $_.FullName.Substring($packageFullPath.Length).TrimStart('\', '/').Replace('\', '/')
            }
    }
)
$expectedJusticeAssetNames = @(
    "Assets/Justice/immatriculation.png",
    "Assets/Justice/tenue.png",
    "Assets/Justice/mandat.png"
)
if ($actualJusticeAssetNames.Count -ne $expectedJusticeAssetNames.Count -or
    @($actualJusticeAssetNames | Where-Object { $expectedJusticeAssetNames -notcontains $_ }).Count -gt 0) {
    throw "Le package doit contenir exactement les trois assets Justice attendus."
}

if (@(Get-ChildItem `
        -LiteralPath $packageFullPath `
        -Filter "NIBScriptHookVDotNet3.dll" `
        -File `
        -Recurse).Count -gt 0) {
    throw "NIBScriptHookVDotNet3.dll est un prerequis runtime et ne doit pas etre package."
}

$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($binary.Path).Version.ToString()
$informationalVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($binary.Path).ProductVersion
$scriptApiReference = Get-ScriptApiReferenceMetadata -BinaryPath $binary.Path
if ($assemblyVersion -ne [string]$manifest.assemblyVersion -or
    $informationalVersion -ne [string]$manifest.informationalVersion -or
    $informationalVersion.IndexOf([string]$manifest.commit, [System.StringComparison]::OrdinalIgnoreCase) -lt 0 -or
    $scriptApiReference.Name -ne [string]$scriptApi.name -or
    $scriptApiReference.Version -ne [string]$scriptApi.version -or
    $scriptApiReference.Major -ne [int]$scriptApi.major) {
    throw "Metadonnees d'assembly incompatibles avec le manifest."
}

if (-not [string]::Equals(
        [string]$abiContract.id,
        $canonicalAbiContract.Id,
        [System.StringComparison]::Ordinal) -or
    -not [string]::Equals(
        [string]$abiContract.version,
        $canonicalAbiContract.Version,
        [System.StringComparison]::Ordinal) -or
    -not [string]::Equals(
        ([string]$abiContract.sha256).ToUpperInvariant(),
        $canonicalAbiContract.Sha256,
        [System.StringComparison]::Ordinal)) {
    throw "Le manifest ne reference pas le contrat ABI canonique attendu."
}

$runtimeApiName = [string]$scriptApi.name
if ($runtimeApiName -ne "NIBScriptHookVDotNet2" -and
    $runtimeApiName -ne "ScriptHookVDotNet2") {
    throw "Nom d'API runtime non autorise dans le manifest: $runtimeApiName"
}
$runtimeApiPath = Join-Path $gtaRootFullPath ($runtimeApiName + ".dll")
if (-not (Test-Path -LiteralPath $runtimeApiPath -PathType Leaf)) {
    throw "API runtime attendue introuvable avant deploiement: $runtimeApiPath"
}
$runtimeApiAssemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($runtimeApiPath)
if ($runtimeApiAssemblyName.Name -ne $runtimeApiName -or
    $null -eq $runtimeApiAssemblyName.Version -or
    $runtimeApiAssemblyName.Version.ToString() -ne [string]$scriptApi.version) {
    throw "L'API runtime installee ne correspond pas a l'identite exigee par le package."
}

# Je bloque le deploiement avant toute ecriture si le runtime GTA ne peut pas
# resoudre exactement les types et membres references par le livrable.
[void](Invoke-AbiValidator `
    -ValidatorPath $abiValidatorFullPath `
    -Arguments @(
        "verify",
        "--consumer", $binary.Path,
        "--contract", $abiContractFullPath,
        "--runtime-api", $runtimeApiPath) `
    -Operation "compatibilite avec l'API runtime GTA")

# Le provider est optionnel : s'il existe sur la cible, je valide strictement sa
# forme avant toute écriture. Son absence active le fallback natif du module.
$hudRuntime = $null
$installedHudRendererPath = Join-Path $gtaRootFullPath "NIBScriptHookVDotNet3.dll"
if (Test-Path -LiteralPath $installedHudRendererPath -PathType Leaf) {
    $runtimeHudDescriptor = [pscustomobject]@{
        assemblyName = "NIBScriptHookVDotNet3"
        minimumMajor = 3
        typeName = "GTA.UI.CustomSprite"
        contractVersion = 1
    }
    $hudRuntime = Assert-HudRendererRuntime `
        -GtaDirectory $gtaRootFullPath `
        -Descriptor $runtimeHudDescriptor
}

$targetJusticeDirectory = Join-Path $scriptsFullPath "Assets\Justice"
if (Test-Path -LiteralPath $targetJusticeDirectory -PathType Leaf) {
    throw "La destination des assets Justice est un fichier: $targetJusticeDirectory"
}
foreach ($justiceAsset in $justiceAssets) {
    $candidateTarget = Join-Path $targetJusticeDirectory ([System.IO.Path]::GetFileName($justiceAsset.Path))
    if (Test-Path -LiteralPath $candidateTarget -PathType Container) {
        throw "La destination d'un asset Justice est un dossier: $candidateTarget"
    }
}

# Je refuse tout déploiement à chaud avant la première création ou écriture dans
# Scripts. Ce garde-fou ne termine jamais lui-même un processus utilisateur.
Assert-GameScriptHostsStopped

$scriptsDirectoryExisted = Test-Path -LiteralPath $scriptsFullPath -PathType Container
$targetAssetsDirectory = Join-Path $scriptsFullPath "Assets"
$assetsDirectoryExisted = Test-Path -LiteralPath $targetAssetsDirectory -PathType Container
New-Item -ItemType Directory -Force -Path $scriptsFullPath | Out-Null
$justiceDirectoryExisted = Test-Path -LiteralPath $targetJusticeDirectory -PathType Container
New-Item -ItemType Directory -Force -Path $targetJusticeDirectory | Out-Null

$transactionId = [System.Guid]::NewGuid().ToString("N")
$stagedBinary = Join-Path $scriptsFullPath (".DonJCustomNpcPlacer." + $transactionId + ".ENdll.staged")
$stagedPdb = Join-Path $scriptsFullPath (".DonJCustomNpcPlacer." + $transactionId + ".pdb.staged")
$stagedManifest = Join-Path $scriptsFullPath (".DonJCustomNpcPlacer." + $transactionId + ".manifest.json.staged")
$binaryBackup = Join-Path $scriptsFullPath (".DonJCustomNpcPlacer." + $transactionId + ".ENdll.previous")
$pdbBackup = Join-Path $scriptsFullPath (".DonJCustomNpcPlacer." + $transactionId + ".pdb.previous")
$manifestBackup = Join-Path $scriptsFullPath (".DonJCustomNpcPlacer." + $transactionId + ".manifest.json.previous")
$targetBinary = Join-Path $scriptsFullPath "DonJCustomNpcPlacer.ENdll"
$targetPdb = Join-Path $scriptsFullPath "DonJCustomNpcPlacer.pdb"
$targetManifest = Join-Path $scriptsFullPath "DonJCustomNpcPlacer.manifest.json"

$assetTransactions = New-Object System.Collections.Generic.List[object]
for ($assetIndex = 0; $assetIndex -lt $justiceAssets.Count; $assetIndex++) {
    $asset = $justiceAssets[$assetIndex]
    $assetFileName = [System.IO.Path]::GetFileName($asset.Path)
    $assetTransactions.Add([pscustomobject]@{
        PackagePath = $asset.Path
        Hash = $asset.Hash
        Length = $asset.Length
        TargetPath = Join-Path $targetJusticeDirectory $assetFileName
        StagedPath = Join-Path $scriptsFullPath (".DonJCustomNpcPlacer." + $transactionId + ".asset-" + $assetIndex + ".staged")
        BackupPath = Join-Path $scriptsFullPath (".DonJCustomNpcPlacer." + $transactionId + ".asset-" + $assetIndex + ".previous")
        Installed = $false
        HadOriginal = $false
    })
}

$obsoleteFiles = @(
    "DonJCustomNpcPlacer.dll",
    "DonJEnemySpawner.dll",
    "DonJEnemySpawner.ENdll",
    "DonJEnemySpawner.pdb"
)
$obsoleteAliasTransactions = New-Object System.Collections.Generic.List[object]
for ($obsoleteIndex = 0; $obsoleteIndex -lt $obsoleteFiles.Count; $obsoleteIndex++) {
    $obsoleteAliasTransactions.Add([pscustomobject]@{
        TargetPath = Join-Path $scriptsFullPath $obsoleteFiles[$obsoleteIndex]
        BackupPath = Join-Path $scriptsFullPath (".DonJCustomNpcPlacer." + $transactionId + ".legacy-" + $obsoleteIndex + ".previous")
        Moved = $false
    })
}

$binaryInstalled = $false
$pdbInstalled = $false
$manifestInstalled = $false
$binaryHadOriginal = $false
$pdbHadOriginal = $false
$manifestHadOriginal = $false
$deploymentCompleted = $false

try {
    Copy-Item -LiteralPath $binary.Path -Destination $stagedBinary
    Copy-Item -LiteralPath $symbols.Path -Destination $stagedPdb
    Copy-Item -LiteralPath $manifestPath -Destination $stagedManifest
    foreach ($assetTransaction in $assetTransactions) {
        Copy-Item -LiteralPath $assetTransaction.PackagePath -Destination $assetTransaction.StagedPath
    }

    if ((Get-Sha256 -Path $stagedBinary) -ne $binary.Hash -or
        (Get-Sha256 -Path $stagedPdb) -ne $symbols.Hash -or
        (Get-Sha256 -Path $stagedManifest) -ne $manifestHash) {
        throw "Le staging GTA ne correspond pas au package verifie."
    }
    foreach ($assetTransaction in $assetTransactions) {
        if ((Get-Sha256 -Path $assetTransaction.StagedPath) -ne $assetTransaction.Hash -or
            (Get-Item -LiteralPath $assetTransaction.StagedPath).Length -ne $assetTransaction.Length) {
            throw "Le staging d'un asset Justice ne correspond pas au package verifie."
        }
    }

    try {
        $binaryHadOriginal = Backup-ExistingTargetFile `
            -TargetPath $targetBinary `
            -BackupPath $binaryBackup
        $binaryInstalled = $true
        Install-StagedFile `
            -StagedPath $stagedBinary `
            -TargetPath $targetBinary `
            -BackupPath $binaryBackup `
            -HadOriginal $binaryHadOriginal
        if ((Get-Sha256 -Path $targetBinary) -ne $binary.Hash) {
            throw "Le binaire GTA ne correspond pas au package apres remplacement."
        }

        $pdbHadOriginal = Backup-ExistingTargetFile `
            -TargetPath $targetPdb `
            -BackupPath $pdbBackup
        $pdbInstalled = $true
        Install-StagedFile `
            -StagedPath $stagedPdb `
            -TargetPath $targetPdb `
            -BackupPath $pdbBackup `
            -HadOriginal $pdbHadOriginal
        if ((Get-Sha256 -Path $targetPdb) -ne $symbols.Hash) {
            throw "Le PDB GTA ne correspond pas au package apres remplacement."
        }

        foreach ($assetTransaction in $assetTransactions) {
            $assetTransaction.HadOriginal = Backup-ExistingTargetFile `
                -TargetPath $assetTransaction.TargetPath `
                -BackupPath $assetTransaction.BackupPath
            $assetTransaction.Installed = $true
            Install-StagedFile `
                -StagedPath $assetTransaction.StagedPath `
                -TargetPath $assetTransaction.TargetPath `
                -BackupPath $assetTransaction.BackupPath `
                -HadOriginal $assetTransaction.HadOriginal
            if ((Get-Sha256 -Path $assetTransaction.TargetPath) -ne $assetTransaction.Hash) {
                throw "Un asset Justice GTA ne correspond pas au package apres remplacement."
            }
        }

        # Je garde les anciens alias disponibles jusqu'à ce que le binaire, le PDB
        # et les trois assets soient installés et relus avec leur SHA-256.
        foreach ($obsoleteAliasTransaction in $obsoleteAliasTransactions) {
            if (Test-Path -LiteralPath $obsoleteAliasTransaction.TargetPath -PathType Leaf) {
                [System.IO.File]::Move(
                    $obsoleteAliasTransaction.TargetPath,
                    $obsoleteAliasTransaction.BackupPath)
                $obsoleteAliasTransaction.Moved = $true
            }
        }
        foreach ($obsoleteAliasTransaction in $obsoleteAliasTransactions) {
            if (Test-Path -LiteralPath $obsoleteAliasTransaction.TargetPath -PathType Leaf) {
                throw "Un ancien alias est reapparu pendant le deploiement: $($obsoleteAliasTransaction.TargetPath)"
            }
        }

        # Je publie volontairement le manifest en dernier : sa présence atteste
        # que le binaire, les symboles, les assets et le nettoyage sont terminés.
        $manifestHadOriginal = Backup-ExistingTargetFile `
            -TargetPath $targetManifest `
            -BackupPath $manifestBackup
        $manifestInstalled = $true
        Install-StagedFile `
            -StagedPath $stagedManifest `
            -TargetPath $targetManifest `
            -BackupPath $manifestBackup `
            -HadOriginal $manifestHadOriginal
        if ((Get-Sha256 -Path $targetManifest) -ne $manifestHash) {
            throw "Le manifest GTA ne correspond pas au package apres remplacement."
        }
        $deploymentCompleted = $true
    }
    catch {
        $deploymentFailure = $_
        $rollbackFailures = New-Object System.Collections.Generic.List[string]

        if ($manifestInstalled) {
            try {
                Restore-PreviousFile `
                    -TargetPath $targetManifest `
                    -BackupPath $manifestBackup `
                    -HadOriginal $manifestHadOriginal
                $manifestInstalled = $false
            }
            catch {
                $rollbackFailures.Add("manifest: $($_.Exception.Message)")
            }
        }

        # Je restaure d'abord les alias pour qu'une version chargeable reste
        # disponible pendant tout le rollback du nouveau livrable.
        for ($obsoleteIndex = $obsoleteAliasTransactions.Count - 1; $obsoleteIndex -ge 0; $obsoleteIndex--) {
            $obsoleteAliasTransaction = $obsoleteAliasTransactions[$obsoleteIndex]
            if (-not $obsoleteAliasTransaction.Moved) {
                continue
            }

            try {
                if (Test-Path -LiteralPath $obsoleteAliasTransaction.TargetPath -PathType Leaf) {
                    throw "la destination existe deja: $($obsoleteAliasTransaction.TargetPath)"
                }
                if (-not (Test-Path -LiteralPath $obsoleteAliasTransaction.BackupPath -PathType Leaf)) {
                    throw "backup absent: $($obsoleteAliasTransaction.BackupPath)"
                }

                [System.IO.File]::Move(
                    $obsoleteAliasTransaction.BackupPath,
                    $obsoleteAliasTransaction.TargetPath)
                $obsoleteAliasTransaction.Moved = $false
            }
            catch {
                $rollbackFailures.Add("alias $([System.IO.Path]::GetFileName($obsoleteAliasTransaction.TargetPath)): $($_.Exception.Message)")
            }
        }

        for ($assetIndex = $assetTransactions.Count - 1; $assetIndex -ge 0; $assetIndex--) {
            $assetTransaction = $assetTransactions[$assetIndex]
            if (-not $assetTransaction.Installed) {
                continue
            }

            try {
                Restore-PreviousFile `
                    -TargetPath $assetTransaction.TargetPath `
                    -BackupPath $assetTransaction.BackupPath `
                    -HadOriginal $assetTransaction.HadOriginal
                $assetTransaction.Installed = $false
            }
            catch {
                $rollbackFailures.Add("asset $([System.IO.Path]::GetFileName($assetTransaction.TargetPath)): $($_.Exception.Message)")
            }
        }

        if ($pdbInstalled) {
            try {
                Restore-PreviousFile `
                    -TargetPath $targetPdb `
                    -BackupPath $pdbBackup `
                    -HadOriginal $pdbHadOriginal
            }
            catch {
                $rollbackFailures.Add("PDB: $($_.Exception.Message)")
            }
        }

        if ($binaryInstalled) {
            try {
                Restore-PreviousFile `
                    -TargetPath $targetBinary `
                    -BackupPath $binaryBackup `
                    -HadOriginal $binaryHadOriginal
            }
            catch {
                $rollbackFailures.Add("ENdll: $($_.Exception.Message)")
            }
        }

        if (-not $justiceDirectoryExisted -and
            (Test-Path -LiteralPath $targetJusticeDirectory -PathType Container) -and
            @(Get-ChildItem -LiteralPath $targetJusticeDirectory -Force).Count -eq 0) {
            Remove-Item -LiteralPath $targetJusticeDirectory -Force
        }

        if ($rollbackFailures.Count -gt 0) {
            throw "Deploiement refuse: $($deploymentFailure.Exception.Message) Rollback incomplet: $($rollbackFailures -join '; ')"
        }

        throw $deploymentFailure
    }

    # Je supprime les backups seulement après la publication et la relecture du manifest final.
    foreach ($obsoleteAliasTransaction in $obsoleteAliasTransactions) {
        if ($obsoleteAliasTransaction.Moved -and
            (Test-Path -LiteralPath $obsoleteAliasTransaction.BackupPath -PathType Leaf)) {
            Remove-Item -LiteralPath $obsoleteAliasTransaction.BackupPath -Force
            $obsoleteAliasTransaction.Moved = $false
        }
    }
    foreach ($assetTransaction in $assetTransactions) {
        if (Test-Path -LiteralPath $assetTransaction.BackupPath -PathType Leaf) {
            Remove-Item -LiteralPath $assetTransaction.BackupPath -Force
        }
    }
    foreach ($backupPath in @($binaryBackup, $pdbBackup, $manifestBackup)) {
        if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }

    Write-Host "Package deploye et verifie vers: $targetBinary"
    Write-Host "SHA-256 ENdll: $($binary.Hash)"
    Write-Host "Assets Justice deployes: $targetJusticeDirectory"
    Write-Host "Manifest deploye en dernier: $targetManifest"
    Write-Host "API ScriptHookVDotNet: $($scriptApiReference.Name) $($scriptApiReference.Version)"
    if ($null -eq $hudRuntime) {
        Write-Host "Renderer HUD: fallback natif (NIBScriptHookVDotNet3 absent)."
    }
    else {
        Write-Host "Renderer HUD: $($hudRuntime.Name) $($hudRuntime.Version) / $($hudRuntime.TypeName)"
    }
    Write-Host "Contrat ABI: $($canonicalAbiContract.Id) $($canonicalAbiContract.Version) $($canonicalAbiContract.Sha256)"
}
finally {
    $temporaryPaths = @($stagedBinary, $stagedPdb, $stagedManifest)
    $temporaryPaths += @($assetTransactions | ForEach-Object { $_.StagedPath })
    foreach ($temporaryPath in $temporaryPaths) {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
    if (-not $deploymentCompleted -and
        -not $justiceDirectoryExisted -and
        (Test-Path -LiteralPath $targetJusticeDirectory -PathType Container) -and
        @(Get-ChildItem -LiteralPath $targetJusticeDirectory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $targetJusticeDirectory -Force
    }
    if (-not $deploymentCompleted -and
        -not $assetsDirectoryExisted -and
        (Test-Path -LiteralPath $targetAssetsDirectory -PathType Container) -and
        @(Get-ChildItem -LiteralPath $targetAssetsDirectory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $targetAssetsDirectory -Force
    }
    if (-not $deploymentCompleted -and
        -not $scriptsDirectoryExisted -and
        (Test-Path -LiteralPath $scriptsFullPath -PathType Container) -and
        @(Get-ChildItem -LiteralPath $scriptsFullPath -Force).Count -eq 0) {
        Remove-Item -LiteralPath $scriptsFullPath -Force
    }
}

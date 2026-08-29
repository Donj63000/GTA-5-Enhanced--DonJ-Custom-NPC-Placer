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

function Install-StagedFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StagedPath,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [Parameter(Mandatory = $true)]
        [string]$BackupPath
    )

    if (Test-Path -LiteralPath $TargetPath -PathType Leaf) {
        [System.IO.File]::Replace($StagedPath, $TargetPath, $BackupPath, $true)
        return $true
    }

    [System.IO.File]::Move($StagedPath, $TargetPath)
    return $false
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
    [bool]$manifest.sourceDirty) {
    throw "Manifest game-ready invalide: $manifestPath"
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

New-Item -ItemType Directory -Force -Path $scriptsFullPath | Out-Null

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

try {
    Copy-Item -LiteralPath $binary.Path -Destination $stagedBinary
    Copy-Item -LiteralPath $symbols.Path -Destination $stagedPdb
    Copy-Item -LiteralPath $manifestPath -Destination $stagedManifest

    if ((Get-Sha256 -Path $stagedBinary) -ne $binary.Hash -or
        (Get-Sha256 -Path $stagedPdb) -ne $symbols.Hash -or
        (Get-Sha256 -Path $stagedManifest) -ne $manifestHash) {
        throw "Le staging GTA ne correspond pas au package verifie."
    }

    try {
        $binaryHadOriginal = Install-StagedFile `
            -StagedPath $stagedBinary `
            -TargetPath $targetBinary `
            -BackupPath $binaryBackup
        $binaryInstalled = $true

        if ((Get-Sha256 -Path $targetBinary) -ne $binary.Hash) {
            throw "Le binaire GTA ne correspond pas au package apres remplacement."
        }

        $pdbHadOriginal = Install-StagedFile `
            -StagedPath $stagedPdb `
            -TargetPath $targetPdb `
            -BackupPath $pdbBackup
        $pdbInstalled = $true

        if ((Get-Sha256 -Path $targetPdb) -ne $symbols.Hash) {
            throw "Le PDB GTA ne correspond pas au package apres remplacement."
        }

        $manifestHadOriginal = Install-StagedFile `
            -StagedPath $stagedManifest `
            -TargetPath $targetManifest `
            -BackupPath $manifestBackup
        $manifestInstalled = $true

        if ((Get-Sha256 -Path $targetManifest) -ne $manifestHash) {
            throw "Le manifest GTA ne correspond pas au package apres remplacement."
        }

        # Je publie et relis d'abord le triplet canonique. Chaque ancien alias
        # reste donc chargeable jusqu'a ce que le nouvel ENdll soit deja valide.
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
    }
    catch {
        $deploymentFailure = $_
        $rollbackFailures = New-Object System.Collections.Generic.List[string]

        # Je restaure les alias avant de retirer le nouvel ENdll. Meme pendant le
        # rollback, au moins une version chargeable reste ainsi presente.
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

        if ($manifestInstalled) {
            try {
                Restore-PreviousFile `
                    -TargetPath $targetManifest `
                    -BackupPath $manifestBackup `
                    -HadOriginal $manifestHadOriginal
            }
            catch {
                $rollbackFailures.Add("manifest: $($_.Exception.Message)")
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

        if ($rollbackFailures.Count -gt 0) {
            throw "Deploiement refuse: $($deploymentFailure.Exception.Message) Rollback incomplet: $($rollbackFailures -join '; ')"
        }

        throw $deploymentFailure
    }

    # Je supprime seulement les copies cachees une fois le nouveau triplet relu et tous les alias absents.
    foreach ($obsoleteAliasTransaction in $obsoleteAliasTransactions) {
        if ($obsoleteAliasTransaction.Moved -and
            (Test-Path -LiteralPath $obsoleteAliasTransaction.BackupPath -PathType Leaf)) {
            Remove-Item -LiteralPath $obsoleteAliasTransaction.BackupPath -Force
            $obsoleteAliasTransaction.Moved = $false
        }
    }

    foreach ($backupPath in @($binaryBackup, $pdbBackup, $manifestBackup)) {
        if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }

    Write-Host "Package deploye et verifie vers: $targetBinary"
    Write-Host "SHA-256 ENdll: $($binary.Hash)"
    Write-Host "Manifest deploye: $targetManifest"
    Write-Host "API ScriptHookVDotNet: $($scriptApiReference.Name) $($scriptApiReference.Version)"
    Write-Host "Contrat ABI: $($canonicalAbiContract.Id) $($canonicalAbiContract.Version) $($canonicalAbiContract.Sha256)"
}
finally {
    foreach ($temporaryPath in @($stagedBinary, $stagedPdb, $stagedManifest)) {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

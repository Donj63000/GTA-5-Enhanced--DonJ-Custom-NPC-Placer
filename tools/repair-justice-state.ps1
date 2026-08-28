[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [Parameter(Mandatory = $true)]
    [string]$StatePath,

    [string]$BackupRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-JusticeXml {
    param([Parameter(Mandatory = $true)][string]$Path)

    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $document = New-Object System.Xml.XmlDocument
    $document.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $document.Load($reader)
    }
    finally {
        $reader.Dispose()
    }
    return $document
}

function Set-JusticeAttribute {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlElement]$Element,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value
    )

    $Element.SetAttribute($Name, $Value)
}

function Get-JusticeBooleanAttribute {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlElement]$Element,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $value = $Element.GetAttribute($Name)
    if ($value -ne "true" -and $value -ne "false") {
        throw "Attribut booléen Justice invalide: $Name='$value'."
    }
    return $value -eq "true"
}

function Get-JusticeFileHash {
    param([Parameter(Mandatory = $true)][string]$Path)

    # Je calcule le hash avec .NET pour que l'outil reste utilisable même si le
    # module Microsoft.PowerShell.Utility n'est pas chargé dans le processus hôte.
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $bytes = $algorithm.ComputeHash($stream)
        return [pscustomobject]@{
            Path = [System.IO.Path]::GetFullPath($Path)
            Hash = ([System.BitConverter]::ToString($bytes)).Replace("-", "")
        }
    }
    finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

function Write-JusticeXmlAtomic {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlDocument]$Document,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $temporaryPath = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    $replacementBackupPath = "$Path.$([Guid]::NewGuid().ToString('N')).replace-backup"
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.Indent = $true
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Entitize
    try {
        $writer = [System.Xml.XmlWriter]::Create($temporaryPath, $settings)
        try {
            $Document.Save($writer)
        }
        finally {
            $writer.Dispose()
        }

        # Je relis le fichier temporaire avant de remplacer l'état utilisé par le jeu.
        $null = Read-JusticeXml -Path $temporaryPath
        if (Test-Path -LiteralPath $Path) {
            [System.IO.File]::Replace($temporaryPath, $Path, $replacementBackupPath, $true)
            Remove-Item -LiteralPath $replacementBackupPath -Force
        }
        else {
            [System.IO.File]::Move($temporaryPath, $Path)
        }
        $temporaryPath = $null
    }
    finally {
        if ($temporaryPath -and (Test-Path -LiteralPath $temporaryPath)) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
        if (Test-Path -LiteralPath $replacementBackupPath) {
            Remove-Item -LiteralPath $replacementBackupPath -Force
        }
    }
}

$resolvedStatePath = [System.IO.Path]::GetFullPath($StatePath)
if (-not (Test-Path -LiteralPath $resolvedStatePath -PathType Leaf)) {
    throw "État Justice introuvable: $resolvedStatePath"
}
if ([System.IO.Path]::GetFileName($resolvedStatePath) -ne "_justice_state.xml") {
    throw "Le fichier ciblé doit être nommé _justice_state.xml."
}

$document = Read-JusticeXml -Path $resolvedStatePath
$root = $document.DocumentElement
if ($null -eq $root -or $root.Name -ne "JusticeState" -or $root.GetAttribute("version") -ne "1") {
    throw "Seul un état Justice v1 canonique peut être réparé."
}

$caseNodes = $root.SelectNodes("Case")
$recordNodes = $root.SelectNodes("Record")
$custodyNodes = $root.SelectNodes("Custody")
if ($caseNodes.Count -ne 1 -or $recordNodes.Count -ne 1 -or $custodyNodes.Count -ne 1) {
    throw "L'état doit contenir exactement un Case, un Record et un Custody."
}

$case = [System.Xml.XmlElement]$caseNodes[0]
$custody = [System.Xml.XmlElement]$custodyNodes[0]
$snapshotNodes = $custody.SelectNodes("InventorySnapshot")
$unsafeInventoryState =
    (Get-JusticeBooleanAttribute -Element $custody -Name "inventoryRemoved") -or
    (Get-JusticeBooleanAttribute -Element $custody -Name "weaponControlsLocked") -or
    (Get-JusticeBooleanAttribute -Element $custody -Name "deferredInventoryRestore") -or
    $snapshotNodes.Count -ne 0
if ($unsafeInventoryState) {
    throw "JUSTICE_REPAIR_UNSAFE_INVENTORY: Réparation refusée: un inventaire retiré, verrouillé ou en reprise doit être restauré par le runtime."
}

$stateDirectory = [System.IO.Path]::GetDirectoryName($resolvedStatePath)
if ([string]::IsNullOrWhiteSpace($BackupRoot)) {
    $BackupRoot = Join-Path $stateDirectory "_justice_recovery_backups"
}
$resolvedBackupRoot = [System.IO.Path]::GetFullPath($BackupRoot)
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDirectory = Join-Path $resolvedBackupRoot $timestamp
$suffix = 0
while (Test-Path -LiteralPath $backupDirectory) {
    $suffix++
    $backupDirectory = Join-Path $resolvedBackupRoot ("{0}-{1}" -f $timestamp, $suffix)
}

if (-not $PSCmdlet.ShouldProcess($resolvedStatePath, "Sauvegarder puis annuler uniquement le dossier Justice actif")) {
    return
}

New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
$backupPrimary = Join-Path $backupDirectory "_justice_state.xml"
$backupSecondary = Join-Path $backupDirectory "_justice_state.xml.bak"
Copy-Item -LiteralPath $resolvedStatePath -Destination $backupPrimary -Force
$secondaryPath = "$resolvedStatePath.bak"
if (Test-Path -LiteralPath $secondaryPath -PathType Leaf) {
    Copy-Item -LiteralPath $secondaryPath -Destination $backupSecondary -Force
}

$beforeHashes = @(
    Get-JusticeFileHash -Path $backupPrimary
)
if (Test-Path -LiteralPath $backupSecondary -PathType Leaf) {
    $beforeHashes += Get-JusticeFileHash -Path $backupSecondary
}

# Je conserve intégralement Record et son indice de récidive; je vide seulement l'affaire active.
Set-JusticeAttribute -Element $root -Name "enabled" -Value "true"
Set-JusticeAttribute -Element $root -Name "pendingDeathCapture" -Value "false"
Set-JusticeAttribute -Element $root -Name "pendingDeathCapturePlayerSlot" -Value "-1"
Set-JusticeAttribute -Element $root -Name "pendingDeathCapturePlayerModel" -Value "0"
Set-JusticeAttribute -Element $root -Name "pendingAmnestyWantedClear" -Value "true"

$caseValues = [ordered]@{
    enabled = "true"
    activeScore = "0"
    fineDue = "0"
    sentenceSeconds = "0"
    hasWarrant = "false"
    phase = "AtLarge"
    wantedEpisodeId = ""
    custodyEpisodeId = ""
    lastCrimeKind = ""
    lastCrimeLabel = ""
    fleeingCharged = "false"
    escapeCharged = "false"
}
foreach ($entry in $caseValues.GetEnumerator()) {
    Set-JusticeAttribute -Element $case -Name $entry.Key -Value $entry.Value
}
while ($null -ne $case.FirstChild) {
    $null = $case.RemoveChild($case.FirstChild)
}
foreach ($containerName in @(
    "Charges",
    "FleeingEpisodes",
    "EscapeEpisodes",
    "ProcessedIncidents",
    "CompletedOperations")) {
    $null = $case.AppendChild($document.CreateElement($containerName))
}

$custodyValues = [ordered]@{
    active = "false"
    site = "None"
    initialSentenceSeconds = "0"
    activityReductionSeconds = "0"
    inventoryRemoved = "false"
    weaponControlsLocked = "false"
    deferredInventoryRestore = "false"
    waitingForRespawn = "false"
    deathRebindPending = "false"
    playerStateStored = "false"
    storedInvincible = "false"
    storedFrozen = "false"
    storedCanRagdoll = "true"
    playerModelHash = "0"
    playerSlot = "-1"
    releaseSelectedWeapon = "-1569615261"
    policeSuppressionApplied = "false"
    policeDispatchDisabled = "false"
}
foreach ($entry in $custodyValues.GetEnumerator()) {
    Set-JusticeAttribute -Element $custody -Name $entry.Key -Value $entry.Value
}
while ($null -ne $custody.FirstChild) {
    $null = $custody.RemoveChild($custody.FirstChild)
}

Write-JusticeXmlAtomic -Document $document -Path $resolvedStatePath
$repairedDocument = Read-JusticeXml -Path $resolvedStatePath
Write-JusticeXmlAtomic -Document $repairedDocument -Path $secondaryPath

$afterHashes = @(
    Get-JusticeFileHash -Path $resolvedStatePath
    Get-JusticeFileHash -Path $secondaryPath
)
$manifest = [ordered]@{
    repairedAt = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss zzz")
    statePath = $resolvedStatePath
    recordPreserved = $true
    recidivism = $recordNodes[0].GetAttribute("recidivism")
    justiceEnabled = $true
    before = @($beforeHashes | ForEach-Object {
        [ordered]@{ path = $_.Path; sha256 = $_.Hash }
    })
    after = @($afterHashes | ForEach-Object {
        [ordered]@{ path = $_.Path; sha256 = $_.Hash }
    })
}
$manifestPath = Join-Path $backupDirectory "manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

[pscustomobject]@{
    StatePath = $resolvedStatePath
    BackupDirectory = $backupDirectory
    PrimaryHash = $afterHashes[0].Hash
    BackupHash = $afterHashes[1].Hash
    RecordPreserved = $true
    JusticeEnabled = $true
}

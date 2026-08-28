param(
    [string]$Title = "bug-report",
    [int]$SinceHours = 24,
    [string]$GtaRoot,
    [string]$LegacyLogRoot,
    [switch]$IncludeFullLogs,
    [switch]$OpenFolder
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$reportsRoot = Join-Path $repoRoot "bug-reports"

function ConvertTo-SafeFileName {
    param([string]$Value)

    $safe = if ([string]::IsNullOrWhiteSpace($Value)) { "bug-report" } else { $Value.Trim() }

    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $safe = $safe.Replace($invalid, "-")
    }

    $safe = ($safe -replace "[^A-Za-z0-9._-]+", "-").Trim("-._")

    if ([string]::IsNullOrWhiteSpace($safe)) {
        return "bug-report"
    }

    if ($safe.Length -gt 72) {
        return $safe.Substring(0, 72).Trim("-._")
    }

    return $safe
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$safeTitle = ConvertTo-SafeFileName $Title
$reportRoot = Join-Path $reportsRoot "$timestamp-$safeTitle"
$rawLogsRoot = Join-Path $reportRoot "raw-logs"
$eventsRoot = Join-Path $reportRoot "windows-events"
$manifestEntries = New-Object System.Collections.Generic.List[object]
$checkedSources = New-Object System.Collections.Generic.List[string]

function Add-UniquePath {
    param(
        [System.Collections.Generic.List[string]]$Target,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    try {
        $fullPath = [System.IO.Path]::GetFullPath($Path)
    }
    catch {
        return
    }

    if (-not $Target.Contains($fullPath)) {
        $Target.Add($fullPath)
    }
}

function Get-ProjectGtaRoots {
    $roots = New-Object System.Collections.Generic.List[string]

    Add-UniquePath $roots $GtaRoot

    $projectPath = Join-Path $repoRoot "src\DonJEnemySpawner\DonJEnemySpawner.csproj"

    if (Test-Path -LiteralPath $projectPath) {
        try {
            [xml]$project = Get-Content -LiteralPath $projectPath -Raw
            $defaultRootProperty = $project.Project.PropertyGroup.DefaultEnhancedGtaRoot | Select-Object -First 1

            if ($defaultRootProperty -ne $null) {
                if ($defaultRootProperty -is [System.Xml.XmlElement]) {
                    Add-UniquePath $roots $defaultRootProperty.InnerText
                }
                else {
                    Add-UniquePath $roots ([string]$defaultRootProperty)
                }
            }
        }
        catch {
        }
    }

    Add-UniquePath $roots "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced"
    Add-UniquePath $roots "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V"

    return $roots
}

function Copy-LogFile {
    param(
        [System.IO.FileInfo]$File,
        [string]$SourceRootLabel
    )

    $checkedSources.Add($File.FullName)

    $targetName = ConvertTo-SafeFileName ($SourceRootLabel + "__" + $File.Name)
    $targetPath = Join-Path $rawLogsRoot $targetName
    $copiedMode = "full"

    try {
        if ($IncludeFullLogs -or $File.Length -le 524288) {
            Copy-Item -LiteralPath $File.FullName -Destination $targetPath -Force
        }
        else {
            $copiedMode = "tail"
            Get-Content -LiteralPath $File.FullName -Tail 4000 -ErrorAction Stop |
                Set-Content -LiteralPath $targetPath -Encoding UTF8
        }

        $manifestEntries.Add([ordered]@{
            source = $File.FullName
            copiedTo = $targetPath
            kind = "log"
            copiedMode = $copiedMode
            length = $File.Length
            lastWriteTime = $File.LastWriteTime.ToString("o")
            status = "copied"
        })
    }
    catch {
        $manifestEntries.Add([ordered]@{
            source = $File.FullName
            copiedTo = $targetPath
            kind = "log"
            copiedMode = $copiedMode
            length = $File.Length
            lastWriteTime = $File.LastWriteTime.ToString("o")
            status = "error"
            error = $_.Exception.Message
        })
    }
}

function Collect-GtaRootLogs {
    param([string]$Root)

    $checkedSources.Add($Root)

    if (-not (Test-Path -LiteralPath $Root)) {
        $manifestEntries.Add([ordered]@{
            source = $Root
            kind = "gta-root"
            status = "missing"
        })
        return
    }

    $label = ConvertTo-SafeFileName ((Split-Path -Leaf $Root) -replace "\s+", "-")
    $rootLogNames = @(
        "NIBScriptHookVDotNet.log",
        "ScriptHookVDotNet.log",
        "ScriptHookV.log",
        "asiloader.log",
        "DirectStorageFix.log",
        "menyooLog.txt",
        "MapEditor.log"
    )

    foreach ($name in $rootLogNames) {
        $path = Join-Path $Root $name

        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Copy-LogFile -File (Get-Item -LiteralPath $path) -SourceRootLabel $label
        }
        else {
            $checkedSources.Add($path)
        }
    }

    $scriptsPath = Join-Path $Root "Scripts"

    if (Test-Path -LiteralPath $scriptsPath) {
        Get-ChildItem -LiteralPath $scriptsPath -File -Filter "*.log" -ErrorAction SilentlyContinue |
            ForEach-Object { Copy-LogFile -File $_ -SourceRootLabel ($label + "__Scripts") }
    }
    else {
        $checkedSources.Add($scriptsPath)
    }
}

function Get-LegacyRuntimeLogRoots {
    $roots = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace($LegacyLogRoot)) {
        Add-UniquePath $roots $LegacyLogRoot
        return $roots
    }

    try {
        $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
        if (-not [string]::IsNullOrWhiteSpace($localAppData)) {
            # Je couvre explicitement le cache shadow-copy .NET où NIB v2 avait
            # caché le journal du crash natif du 26 août.
            Add-UniquePath $roots (Join-Path $localAppData "assembly")
            Add-UniquePath $roots (Join-Path $localAppData "Temp")
        }
    }
    catch {
    }

    try {
        Add-UniquePath $roots ([System.IO.Path]::GetTempPath())
    }
    catch {
    }

    return $roots
}

function Collect-StableRuntimeFallbackLogs {
    $directories = New-Object System.Collections.Generic.List[string]

    try {
        $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
        if (-not [string]::IsNullOrWhiteSpace($localAppData)) {
            Add-UniquePath $directories (Join-Path $localAppData "DonJEnemySpawner\Logs")
            Add-UniquePath $directories (Join-Path $localAppData "DonJEnemySpawner\Saves")
        }
    }
    catch {
    }

    try {
        $documents = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
        if (-not [string]::IsNullOrWhiteSpace($documents)) {
            Add-UniquePath $directories (Join-Path $documents "Rockstar Games\GTAV Enhanced\DonJEnemySpawnerSaves")
        }
    }
    catch {
    }

    foreach ($directory in $directories) {
        $path = Join-Path $directory "DonJCustomNpcPlacer.log"
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            if (-not $checkedSources.Contains([System.IO.Path]::GetFullPath($path))) {
                Copy-LogFile -File (Get-Item -LiteralPath $path) -SourceRootLabel "DonJ-Runtime-Stable"
            }
        }
        else {
            $checkedSources.Add($path)
        }
    }
}

function Collect-LatestLegacyRuntimeLog {
    $candidates = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    $candidatePaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    foreach ($root in Get-LegacyRuntimeLogRoots) {
        $checkedSources.Add($root)
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }

        try {
            foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse -Force -Filter "DonJCustomNpcPlacer.log" -ErrorAction SilentlyContinue) {
                if ($file -ne $null -and
                    -not $checkedSources.Contains($file.FullName) -and
                    $candidatePaths.Add($file.FullName)) {
                    $candidates.Add($file)
                }
            }
        }
        catch {
            # Je poursuis les autres emplacements si un dossier temporaire est verrouillé.
        }
    }

    $latest = $candidates |
        Sort-Object LastWriteTimeUtc, FullName -Descending |
        Select-Object -First 1
    if ($latest -ne $null) {
        Copy-LogFile -File $latest -SourceRootLabel "DonJ-Runtime-Legacy"
    }
}

function Write-RepoState {
    $repoStatePath = Join-Path $reportRoot "repo-state.txt"
    $lines = New-Object System.Collections.Generic.List[string]

    $lines.Add("Repository: $repoRoot")
    $lines.Add("CollectedAt: $((Get-Date).ToString("o"))")
    $lines.Add("")

    foreach ($args in @(
        @("rev-parse", "--abbrev-ref", "HEAD"),
        @("rev-parse", "HEAD"),
        @("status", "--short"),
        @("diff", "--stat")
    )) {
        $lines.Add("> git $($args -join " ")")

        try {
            $output = & git @args 2>&1
            $lines.AddRange([string[]]$output)
        }
        catch {
            $lines.Add("ERREUR: $($_.Exception.Message)")
        }

        $lines.Add("")
    }

    $lines | Set-Content -LiteralPath $repoStatePath -Encoding UTF8
}

function Collect-WindowsEvents {
    $eventsPath = Join-Path $eventsRoot "application-events.json"
    $eventsTextPath = Join-Path $eventsRoot "application-events.txt"
    $startTime = (Get-Date).AddHours(-[Math]::Max(1, $SinceHours))

    try {
        $events = Get-WinEvent -FilterHashtable @{ LogName = "Application"; StartTime = $startTime } -ErrorAction Stop |
            Where-Object {
                $_.ProviderName -match "Application Error|\.NET Runtime|Windows Error Reporting" -or
                $_.Message -match "GTA5|GTA5_Enhanced|ScriptHook|NIBScriptHook|DonJCustomNpcPlacer|DonJEnemySpawner"
            } |
            Select-Object -First 80 TimeCreated, ProviderName, Id, LevelDisplayName, Message

        $events | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $eventsPath -Encoding UTF8
        $events | Format-List | Out-String | Set-Content -LiteralPath $eventsTextPath -Encoding UTF8

        $manifestEntries.Add([ordered]@{
            source = "Windows Application Event Log"
            copiedTo = $eventsPath
            kind = "windows-events"
            status = "copied"
            sinceHours = $SinceHours
            count = @($events).Count
        })
    }
    catch {
        "Impossible de lire le journal Application Windows: $($_.Exception.Message)" |
            Set-Content -LiteralPath $eventsTextPath -Encoding UTF8

        $manifestEntries.Add([ordered]@{
            source = "Windows Application Event Log"
            copiedTo = $eventsTextPath
            kind = "windows-events"
            status = "error"
            error = $_.Exception.Message
        })
    }
}

function Write-SummaryFiles {
    $summaryPath = Join-Path $reportRoot "summary.md"
    $crashEntryPath = Join-Path $reportRoot "crash-list-entry.md"
    $manifestPath = Join-Path $reportRoot "manifest.json"
    $copiedLogs = @($manifestEntries | Where-Object { $_.kind -eq "log" -and $_.status -eq "copied" })
    $eventEntries = @($manifestEntries | Where-Object { $_.kind -eq "windows-events" } | Select-Object -First 1)
    $eventStatus = if ($eventEntries.Count -gt 0) { $eventEntries[0].status } else { "non collecte" }
    $now = Get-Date
    $offset = $now.ToString("zzz")
    $crashTimestamp = $now.ToString("yyyy-MM-dd HH:mm:ss") + " " + $offset

    $manifest = [ordered]@{
        title = $Title
        safeTitle = $safeTitle
        collectedAt = $now.ToString("o")
        sinceHours = $SinceHours
        includeFullLogs = [bool]$IncludeFullLogs
        reportRoot = $reportRoot
        checkedSources = $checkedSources.ToArray()
        entries = $manifestEntries.ToArray()
    }

    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    @(
        "# Rapport bug - $Title",
        "",
        "- Date: $($now.ToString("yyyy-MM-dd HH:mm:ss zzz"))",
        "- Dossier: ``$reportRoot``",
        "- Fenetre logs: $SinceHours heure(s)",
        "- Logs copies: $($copiedLogs.Count)",
        "- Evenements Windows: $eventStatus",
        "",
        "## Sources verifiees",
        ($checkedSources | Sort-Object -Unique | ForEach-Object { "- ``$_``" }),
        "",
        "## Logs copies",
        ($copiedLogs | ForEach-Object { "- ``$($_.source)`` -> ``$($_.copiedTo)`` ($($_.copiedMode), $($_.length) octets)" }),
        "",
        "## A coller dans crash-list.md",
        "Voir ``crash-list-entry.md``."
    ) | Set-Content -LiteralPath $summaryPath -Encoding UTF8

    @(
        "## $crashTimestamp - $Title",
        "- Statut: Ouvert",
        "- Contexte: A completer avec l'action en jeu, la commande ou les etapes de reproduction.",
        "- Symptome: A completer avec le bug observe.",
        "- Sources verifiees:",
        ($checkedSources | Sort-Object -Unique | ForEach-Object { "  - ``$_``" }),
        "- Extraits utiles:",
        ($copiedLogs | Select-Object -First 12 | ForEach-Object { "  - ``$($_.source)``: copie locale ``$($_.copiedTo)``." }),
        "- Analyse / hypothese: A analyser a partir des logs collectes.",
        "- Action menee: Collecte automatique des logs via ``tools\collect-bug-logs.ps1``.",
        "- Verification: A completer apres reproduction ou correctif.",
        "- Resolution: A revoir."
    ) | Set-Content -LiteralPath $crashEntryPath -Encoding UTF8
}

New-Item -ItemType Directory -Force -Path $reportsRoot, $reportRoot, $rawLogsRoot, $eventsRoot | Out-Null

foreach ($root in Get-ProjectGtaRoots) {
    Collect-GtaRootLogs -Root $root
}

Collect-StableRuntimeFallbackLogs
Collect-LatestLegacyRuntimeLog
Collect-WindowsEvents
Write-RepoState
Write-SummaryFiles

Write-Host "Rapport bug cree: $reportRoot"

if ($OpenFolder) {
    Invoke-Item -LiteralPath $reportRoot
}

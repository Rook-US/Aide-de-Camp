param(
    [Parameter(Mandatory=$true)][string]$BeforeSave,
    [Parameter(Mandatory=$true)][string]$AfterSave
)

# Resolve descendants within each save by IDs, then match units by game path identity.
function Read-Snapshot([string]$directory) {
    if (([IO.File]::ReadAllText((Join-Path $directory 'version.dat'))).Trim() -ne '1.142') {
        throw "Unsupported save version: $directory"
    }
    $groupLines = [IO.File]::ReadAllLines((Join-Path $directory 'groups.dat'))
    $unitLines = [IO.File]::ReadAllLines((Join-Path $directory 'regiments.dat'))
    $groupCount = [int]$groupLines[0]
    $unitCount = [int]$unitLines[0]
    if ($groupLines.Length -ne 1 + 32 * $groupCount -or
        $unitLines.Length -ne 1 + 39 * $unitCount) { throw 'Malformed fixed-record count' }
    $groups = @(for($i=0;$i -lt $groupCount;$i++) {
        $s = 1 + 32 * $i
        [pscustomobject]@{
            Id=[int]$groupLines[$s]; Name=$groupLines[$s+1]; Parent=[int]$groupLines[$s+2]
            Faction=[int]$groupLines[$s+3]; Commander=[int]$groupLines[$s+4]
            NativeTier=[int]$groupLines[$s+17]; ArtilleryStore=[double]::Parse($groupLines[$s+7], [Globalization.CultureInfo]::InvariantCulture)
        }
    })
    $roots = @($groups | Where-Object {
        $_.Name -ceq 'I. CORPS' -and $_.Parent -eq -1 -and $_.Faction -eq 0 -and
        $_.Commander -eq 0 -and $_.NativeTier -eq 16
    })
    if ($roots.Count -ne 1) { throw "I. CORPS root missing or ambiguous: $directory" }
    $descendants = @{$roots[0].Id=$true}
    do {
        $oldCount = $descendants.Count
        foreach($g in $groups) {
            if ($descendants.ContainsKey($g.Parent)) { $descendants[$g.Id]=$true }
        }
    } while($oldCount -ne $descendants.Count)
    $paths = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $directory)
    $pathMap = @{}
    foreach($p in $paths) {
        $key = ConvertTo-Json -InputObject @($p.Name,$p.Abbreviation,$p.UnitType,$p.Commander) -Compress
        if ($pathMap.ContainsKey($key)) { throw "Duplicate path identity: $key" }
        $pathMap[$key]=$p
    }
    $artillery = @{}
    for($i=0;$i -lt $unitCount;$i++) {
        $s = 1 + 39 * $i
        if (-not $descendants.ContainsKey([int]$unitLines[$s+3])) { continue }
        if ([int]$unitLines[$s+4] -ne 2) { continue }
        $key = ConvertTo-Json -InputObject @($unitLines[$s+1],$unitLines[$s+2],2,[int]$unitLines[$s+5]) -Compress
        if (-not $pathMap.ContainsKey($key)) { throw "Artillery path identity missing: $key" }
        if ($artillery.ContainsKey($key)) { throw "Duplicate artillery identity: $key" }
        $p = $pathMap[$key]
        $artillery[$key] = [pscustomobject]@{
            Name=$unitLines[$s+1]
            Men=[int]$unitLines[$s+6]
            Wounded=[int]$unitLines[$s+7]
            Stock=[double]::Parse($p.SupplyStock[1], [Globalization.CultureInfo]::InvariantCulture)
        }
    }
    $hqPaths = @($paths | Where-Object {
        $_.Name -ceq 'I. CORPS' -and $_.Abbreviation -ceq '' -and
        $_.UnitType -eq 16 -and $_.Commander -eq 0
    })
    if ($hqPaths.Count -ne 1) { throw 'I. CORPS path missing or ambiguous' }
    return [pscustomobject]@{
        Root=$roots[0]; DescendantCount=$descendants.Count-1; Artillery=$artillery
        HqStock=$hqPaths[0].SupplyStock -join ','
        Groups=@($groups | Where-Object {$descendants.ContainsKey($_.Id)})
    }
}

$before = Read-Snapshot $BeforeSave
$after = Read-Snapshot $AfterSave
if ($before.Artillery.Count -ne $after.Artillery.Count) { throw 'Artillery count changed' }
$rows = foreach($key in $before.Artillery.Keys) {
    if (-not $after.Artillery.ContainsKey($key)) { throw "Artillery unit missing after save: $key" }
    $a = $before.Artillery[$key]
    $b = $after.Artillery[$key]
    [pscustomobject]@{Name=$a.Name; MenBefore=$a.Men; MenAfter=$b.Men; WoundedBefore=$a.Wounded; WoundedAfter=$b.Wounded; StockBefore=$a.Stock; StockAfter=$b.Stock; Delta=$b.Stock-$a.Stock}
}
$meanBefore = ($rows | ForEach-Object { $_.StockBefore / [Math]::Max(1, $_.MenBefore + $_.WoundedBefore) } | Measure-Object -Average).Average * 100
$meanAfter = ($rows | ForEach-Object { $_.StockAfter / [Math]::Max(1, $_.MenAfter + $_.WoundedAfter) } | Measure-Object -Average).Average * 100
Write-Output "I_CORPS_HQ_STOCK before=$($before.HqStock) after=$($after.HqStock)"
Write-Output "I_CORPS_ARTILLERY_STORE before=$($before.Root.ArtilleryStore) after=$($after.Root.ArtilleryStore)"
Write-Output "DESCENDANT_ARTILLERY_MEAN_PERCENT before=$meanBefore after=$meanAfter"
Write-Output "DESCENDANT_GROUPS before=$($before.DescendantCount) after=$($after.DescendantCount)"
Write-Output "ATTACHED_ARTILLERY_UNITS $($rows.Count)"
Write-Output "ARTILLERY_STOCK_INDEX1_SUM before=$(($rows | Measure-Object StockBefore -Sum).Sum) after=$(($rows | Measure-Object StockAfter -Sum).Sum)"
Write-Output "ARTILLERY_STOCK_INDEX1_CHANGED $(@($rows | Where-Object {$_.Delta -ne 0}).Count)"
Write-Output "ARTILLERY_MEN_CHANGED $(@($rows | Where-Object {$_.MenBefore -ne $_.MenAfter}).Count)"
$rows | Where-Object {$_.Delta -ne 0 -or $_.MenBefore -ne $_.MenAfter} |
    Sort-Object Name | Format-Table Name,MenBefore,MenAfter,StockBefore,StockAfter,Delta -AutoSize

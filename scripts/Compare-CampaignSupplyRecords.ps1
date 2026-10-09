param(
    [Parameter(Mandatory=$true)][string]$BeforeSave,
    [Parameter(Mandatory=$true)][string]$AfterSave
)

# Compare two fully parsed 1.142 campaign path files through game unit identity.
function Read-ByIdentity([string]$directory) {
    $records = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $directory)
    $byKey = @{}
    foreach ($record in $records) {
        $key = ConvertTo-Json -InputObject @($record.Name, $record.Abbreviation,
            $record.UnitType, $record.Commander) -Compress
        if ($byKey.ContainsKey($key)) { throw "Duplicate path identity: $key" }
        $byKey[$key] = $record
    }
    return $byKey
}

$before = Read-ByIdentity $BeforeSave
$after = Read-ByIdentity $AfterSave
if ($before.Count -ne $after.Count) { throw 'Path record count changed' }
$rows = foreach ($key in $before.Keys) {
    if (-not $after.ContainsKey($key)) { throw "Path identity missing after save: $key" }
    $a = $before[$key]
    $b = $after[$key]
    [pscustomobject]@{
        Name = $a.Name
        UnitType = $a.UnitType
        Commander = $a.Commander
        FlowBefore = $a.SupplyHeader[3]
        FlowAfter = $b.SupplyHeader[3]
        StockBefore = $a.SupplyStock -join ','
        StockAfter = $b.SupplyStock -join ','
        FlowChanged = $a.SupplyHeader[3] -cne $b.SupplyHeader[3]
        StockChanged = ($a.SupplyStock -join ',') -cne ($b.SupplyStock -join ',')
    }
}
Write-Output "matched_records $($rows.Count)"
Write-Output "flow_changed $(@($rows | Where-Object FlowChanged).Count)"
Write-Output "stock_changed $(@($rows | Where-Object StockChanged).Count)"
$rows | Where-Object { $_.Name -eq 'I. CORPS' -or $_.Name -like 'ADC Test*' } |
    Sort-Object Name | Format-Table Name,UnitType,FlowBefore,FlowAfter,StockBefore,StockAfter -AutoSize

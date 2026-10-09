param([Parameter(Mandatory=$true)][string]$SaveDirectory)

# Read-only investigation of the installed game's 1.142 paths.dat layout.
# Numeric positions below are stream cursors, never persistent unit identities.
$version = ([IO.File]::ReadAllText((Join-Path $SaveDirectory 'version.dat'))).Trim()
if ($version -ne '1.142') { throw "Only observed save version 1.142 is supported by this inspector (found '$version')." }
$lines = [IO.File]::ReadAllLines((Join-Path $SaveDirectory 'paths.dat'))
$script:cursor = 0
function Read-Text {
    if ($script:cursor -ge $lines.Length) { throw "Unexpected end of paths.dat at line $script:cursor" }
    $value = $lines[$script:cursor]
    $script:cursor++
    return $value
}
function Read-Int {
    $value = Read-Text
    $number = 0
    if (![int]::TryParse($value, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
        throw "Invalid integer '$value' at line $($script:cursor - 1)"
    }
    return $number
}
function Read-Count {
    $count = Read-Int
    if ($count -lt 0 -or $count -gt 100000 -or $script:cursor + $count -gt $lines.Length) {
        throw "Invalid count $count at line $($script:cursor - 1)"
    }
    return $count
}
function Skip-Lines([int]$count) {
    if ($count -lt 0 -or $script:cursor + $count -gt $lines.Length) { throw "Invalid skip $count at line $script:cursor" }
    $script:cursor += $count
}
function Read-UnitReference { Skip-Lines 4 }

$recordCount = Read-Count
$records = [Collections.Generic.List[object]]::new()
for ($record = 0; $record -lt $recordCount; $record++) {
    $start = $script:cursor
    $name = Read-Text
    $abbreviation = Read-Text
    $unitType = Read-Int
    $commander = Read-Int
    $coverCount = Read-Count; Skip-Lines $coverCount
    Skip-Lines 1 # rotation
    $pathCount = Read-Count; Skip-Lines (4 * $pathCount)
    Skip-Lines 7 # three times, four battle flags
    $transferLine = $script:cursor
    Skip-Lines 1 # transfer time
    $transferPosition = @($lines[$script:cursor], $lines[$script:cursor + 1], $lines[$script:cursor + 2])
    $patrolStart = @($lines[$script:cursor + 3], $lines[$script:cursor + 4], $lines[$script:cursor + 5])
    Skip-Lines 6 # transfer position, patrol start
    Skip-Lines 1 # isgarrisonbasicunit
    Skip-Lines 1 # blockade efficiency
    Skip-Lines 5 # campaign/cavalry/fleet order modes
    Skip-Lines 4 # idle time, entrenchment, last stats update, morale
    $orderTypeCount = Read-Count; Skip-Lines $orderTypeCount
    Read-UnitReference # sourceunittoadvise
    Skip-Lines 1 # orderstate
    $queueCount = Read-Count
    for ($order = 0; $order -lt $queueCount; $order++) {
        Read-UnitReference # advised unit
        Skip-Lines 7 # path IDs, time fields, order type and rotation
        $receivedCount = Read-Count; Skip-Lines (4 * $receivedCount)
        Read-UnitReference # initial unit that gave order
        $courierCount = Read-Count; Skip-Lines (16 * $courierCount)
    }
    Skip-Lines 4 # upkeep, recruitment cost, retreat flags
    $ammoCount = Read-Count
    $ammunition = @()
    if ($ammoCount -gt 0) {
        $ammunition = @($lines[$script:cursor..($script:cursor + $ammoCount - 1)])
    }
    Skip-Lines $ammoCount
    $consumptionCount = Read-Count
    $consumption = @()
    if ($consumptionCount -gt 0) {
        $consumption = @($lines[$script:cursor..($script:cursor + 3 * $consumptionCount - 1)])
    }
    Skip-Lines (3 * $consumptionCount)
    $supplyHeader = @()
    $supplyStock = @()
    $supplyHeaderLine = -1
    $supplyStockLine = -1
    if ($consumptionCount -gt 0) {
        $supplyHeaderLine = $script:cursor
        $supplyHeader = @($lines[$script:cursor..($script:cursor + 4)])
        Skip-Lines 5 # supply speed, reinforcements, supply state, retreat angle
        $supplyStockLine = $script:cursor
        $supplyStock = @($lines[$script:cursor..($script:cursor + 3)])
        Skip-Lines 4 # supplyinstock array (four values in the 1.142 loader)
    }
    Skip-Lines 7 # finished recruitment through prior wounded
    Skip-Lines 5 # transport route permissions
    $theaterPosition = @($lines[$script:cursor], $lines[$script:cursor + 1], $lines[$script:cursor + 2])
    Skip-Lines 3 # theater position
    Skip-Lines 6 # reinforcement priority through last combat zone count
    Skip-Lines 2 # assigned army group name and commander
    Skip-Lines 1 # assigned army group commander
    Skip-Lines 7 # embarkation, recovery, commander-campaign, coordination
    $records.Add([pscustomobject]@{
        Ordinal=$record; Name=$name; Abbreviation=$abbreviation; UnitType=$unitType;
        Commander=$commander; Start=$start; End=$script:cursor;
        TransferLine=$transferLine; CoverCount=$coverCount; PathCount=$pathCount;
        QueueCount=$queueCount; TransferPosition=$transferPosition;
        PatrolStart=$patrolStart; TheaterPosition=$theaterPosition;
        Ammunition=$ammunition; Consumption=$consumption;
        SupplyHeader=$supplyHeader; SupplyStock=$supplyStock;
        SupplyHeaderLine=$supplyHeaderLine; SupplyStockLine=$supplyStockLine
    })
}
if ($script:cursor -ne $lines.Length) { throw "Parsed $recordCount records but $($lines.Length - $script:cursor) lines remain." }
$records

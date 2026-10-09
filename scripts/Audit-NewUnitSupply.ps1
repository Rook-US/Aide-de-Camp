param([Parameter(Mandatory=$true)][string]$SaveDirectory)

# Resolve each controlled test unit by its saved four-part game identity.
$lines = [IO.File]::ReadAllLines((Join-Path $SaveDirectory 'regiments.dat'))
$count = [int]$lines[0]
if ($lines.Length -ne 1 + 39 * $count) { throw 'Malformed regiments.dat count' }
$records = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $SaveDirectory)
$results = @()
for ($i = 0; $i -lt $count; $i++) {
    $start = 1 + 39 * $i
    $name = $lines[$start + 1]
    if ($name -notlike 'ADC Test*') { continue }
    $abbreviation = $lines[$start + 2]
    $type = [int]$lines[$start + 4]
    $commander = [int]$lines[$start + 5]
    $matching = @($records | Where-Object {
        $_.Name -ceq $name -and $_.Abbreviation -ceq $abbreviation -and
        $_.UnitType -eq $type -and $_.Commander -eq $commander
    })
    if ($matching.Count -ne 1) { throw "No unique path identity for $name" }
    $path = $matching[0]
    $results += [pscustomobject]@{
        Name = $name
        Type = $type
        TotalMen = [int]$lines[$start + 6]
        Ammunition = $path.Ammunition -join ','
        Consumption = $path.Consumption -join ','
        SupplyHeader = $path.SupplyHeader -join ','
        SupplyStock = $path.SupplyStock -join ','
    }
}
if ($results.Count -ne 6) { throw "Expected six controlled test units; found $($results.Count)" }
$results

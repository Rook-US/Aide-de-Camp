$root = 'G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G'
$source = Join-Path $root 'Save7_10_2026_22_35_17'
$target = Join-Path $root 'ADC-TEST-6-INFANTRY-HALF-STOCK'
if (Test-Path $target) { throw "Refusing to overwrite $target" }
if (([IO.File]::ReadAllText((Join-Path $source 'version.dat'))).Trim() -ne '1.142') {
    throw 'Unsupported source version'
}

$records = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $source)
$matching = @($records | Where-Object {
    $_.Name -ceq 'ADC Test 1-1 Infantry' -and
    $_.Abbreviation -ceq 'ADC Test 1-1 Infantry' -and
    $_.UnitType -eq 0 -and $_.Commander -eq 91
})
if ($matching.Count -ne 1) { throw 'Supply target path identity is missing or ambiguous' }
$path = $matching[0]
if ($path.SupplyStockLine -lt 0 -or ($path.SupplyStock -join ',') -ne '999.1487,1000,1000,1000') {
    throw 'Source stock differs from the inspected game-written baseline'
}
$regiments = [IO.File]::ReadAllLines((Join-Path $source 'regiments.dat'))
$count = [int]$regiments[0]
if ($regiments.Length -ne 1 + 39 * $count) { throw 'Malformed regiment count' }
$unit = @(for($i=0;$i -lt $count;$i++) {
    $s = 1 + 39 * $i
    if ($regiments[$s+1] -ceq 'ADC Test 1-1 Infantry' -and
        $regiments[$s+2] -ceq 'ADC Test 1-1 Infantry' -and
        $regiments[$s+4] -eq '0' -and $regiments[$s+5] -eq '91') { $s }
})
if ($unit.Count -ne 1 -or $regiments[$unit[0]+6] -ne '1000') {
    throw 'Target infantry or its saved strength is missing or ambiguous'
}

Copy-Item -LiteralPath $source -Destination $target -Recurse
$sourceLines = [IO.File]::ReadAllLines((Join-Path $source 'paths.dat'))
$edited = [string[]]$sourceLines.Clone()
$edited[$path.SupplyStockLine] = '500'
$edited[$path.SupplyStockLine + 2] = '500'
[IO.File]::WriteAllLines((Join-Path $target 'paths.dat'), $edited, [Text.UTF8Encoding]::new($false))
$scenario = [IO.File]::ReadAllLines((Join-Path $target 'scenario.dat'))
if (($scenario[2..4] -join ',') -ne '11,7,1861') { throw 'Unexpected scenario date' }
$scenario[24] = 'ADC Test 6 - Infantry Half Stock'
[IO.File]::WriteAllLines((Join-Path $target 'scenario.dat'), $scenario, [Text.UTF8Encoding]::new($false))

$actual = [IO.File]::ReadAllLines((Join-Path $target 'paths.dat'))
$diff = @(for($i=0;$i -lt $sourceLines.Length;$i++) {
    if ($sourceLines[$i] -cne $actual[$i]) { $i }
})
if ($actual.Length -ne $sourceLines.Length -or
    ($diff -join ',') -ne "$($path.SupplyStockLine),$($path.SupplyStockLine + 2)") {
    throw "Unexpected paths.dat diff: $($diff -join ',')"
}
$changed = @(Get-ChildItem -LiteralPath $target -File | Where-Object {
    (Get-FileHash -LiteralPath $_.FullName).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $source $_.Name)).Hash
} | Select-Object -ExpandProperty Name | Sort-Object)
if (($changed -join ',') -ne 'paths.dat,scenario.dat') {
    throw "Unexpected changed files: $($changed -join ',')"
}
Write-Output "test_save $target"
Write-Output "target $($path.Name) type=$($path.UnitType) commander=$($path.Commander) total_men=1000"
Write-Output "stock_before $($path.SupplyStock -join ',') stock_in_test 500,1000,500,1000"
Write-Output "changed_files $($changed -join ',')"

param(
    [Parameter(Mandatory=$true)][string]$TypeName,
    [Parameter(Mandatory=$true)][string]$MethodName
)

# Read-only IL view for verifying installed game field reads and calculations.
$managed = 'G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\The Civil War (1861-1865)_Data\Managed'
Get-ChildItem $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch {}
}
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'Assembly-CSharp.dll'))
$type = $assembly.GetType($TypeName)
if ($null -eq $type) { throw "Game type not found: $TypeName" }
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
$methods = if ($MethodName -eq '.ctor') { @($type.GetConstructors($flags) | Where-Object Name -eq '.ctor') } elseif ($MethodName -eq '.cctor') { @($type.TypeInitializer) } else { @($type.GetMethods($flags) | Where-Object Name -eq $MethodName) }
if ($methods.Count -ne 1) { throw "Expected one $TypeName.$MethodName method; found $($methods.Count)" }
$method = $methods[0]
$body = $method.GetMethodBody()
if ($null -eq $body) { throw 'Method has no IL body' }
$bytes = $body.GetILAsByteArray()
$ops = @{}
[Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static') | ForEach-Object {
    $op = $_.GetValue($null)
    $ops[([int]$op.Value -band 65535)] = $op
}
$position = 0
while ($position -lt $bytes.Length) {
    $offset = $position
    $code = [int]$bytes[$position++]
    if ($code -eq 254) { $code = 65024 + [int]$bytes[$position++] }
    $op = $ops[$code]
    if ($null -eq $op) { throw "Unknown IL opcode at $offset" }
    $size = 0
    $operand = ''
    switch ($op.OperandType.ToString()) {
        InlineNone { $size = 0 }
        { $_ -in 'ShortInlineI','ShortInlineVar','ShortInlineBrTarget' } { $size = 1; $operand = [int]$bytes[$position] }
        InlineVar { $size = 2; $operand = [BitConverter]::ToUInt16($bytes, $position) }
        { $_ -in 'InlineI','ShortInlineR','InlineBrTarget','InlineField','InlineMethod','InlineSig','InlineString','InlineTok','InlineType' } { $size = 4 }
        { $_ -in 'InlineI8','InlineR' } { $size = 8 }
        InlineSwitch { $size = 4 + 4 * [BitConverter]::ToInt32($bytes, $position) }
    }
    switch ($op.OperandType.ToString()) {
        InlineI { $operand = [BitConverter]::ToInt32($bytes, $position) }
        ShortInlineR { $operand = [BitConverter]::ToSingle($bytes, $position) }
        InlineR { $operand = [BitConverter]::ToDouble($bytes, $position) }
        InlineString {
            try { $operand = $method.Module.ResolveString([BitConverter]::ToInt32($bytes, $position)) } catch {}
        }
        InlineField {
            try { $value = $method.Module.ResolveField([BitConverter]::ToInt32($bytes, $position)); $operand = "$($value.DeclaringType.Name).$($value.Name)" } catch {}
        }
        InlineMethod {
            try { $value = $method.Module.ResolveMethod([BitConverter]::ToInt32($bytes, $position)); $operand = "$($value.DeclaringType.Name).$($value.Name)" } catch {}
        }
    }
    '{0,5} {1,-18} {2}' -f $offset, $op.Name, $operand
    $position += $size
}

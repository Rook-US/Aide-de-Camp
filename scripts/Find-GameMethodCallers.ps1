param([string[]]$TargetNames=@(), [string[]]$TargetFields=@())

if ($TargetNames.Count -eq 0 -and $TargetFields.Count -eq 0) {
    throw 'Provide at least one method or field name to trace'
}

# Read-only call-reference probe for the locally installed game assembly.
$managed = 'G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\The Civil War (1861-1865)_Data\Managed'
Get-ChildItem $managed -Filter '*.dll' | ForEach-Object {
    try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch {}
}
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'Assembly-CSharp.dll'))
$ops = @{}
[Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static') | ForEach-Object {
    $op = $_.GetValue($null)
    $ops[([int]$op.Value -band 65535)] = $op
}
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
foreach ($type in $assembly.GetTypes()) {
    foreach ($method in @($type.GetMethods($flags)) + @($type.GetConstructors($flags))) {
        $body = $method.GetMethodBody()
        if ($null -eq $body) { continue }
        $bytes = $body.GetILAsByteArray()
        $position = 0
        while ($position -lt $bytes.Length) {
            $code = [int]$bytes[$position++]
            if ($code -eq 254) { $code = 65024 + [int]$bytes[$position++] }
            $op = $ops[$code]
            if ($null -eq $op) { break }
            $size = 0
            switch ($op.OperandType.ToString()) {
                InlineNone { $size = 0 }
                { $_ -in 'ShortInlineI','ShortInlineVar','ShortInlineBrTarget' } { $size = 1 }
                InlineVar { $size = 2 }
                { $_ -in 'InlineI','ShortInlineR','InlineBrTarget','InlineField','InlineMethod','InlineSig','InlineString','InlineTok','InlineType' } { $size = 4 }
                { $_ -in 'InlineI8','InlineR' } { $size = 8 }
                InlineSwitch { $size = 4 + 4 * [BitConverter]::ToInt32($bytes, $position) }
            }
            if ($op.OperandType -eq [Reflection.Emit.OperandType]::InlineMethod) {
                $token = [BitConverter]::ToInt32($bytes, $position)
                try {
                    $callee = $method.Module.ResolveMethod($token)
                    if ($callee.Name -in $TargetNames) {
                        [pscustomobject]@{
                            CallerType = $type.FullName
                            Caller = $method.Name
                            CalleeType = $callee.DeclaringType.FullName
                            Callee = $callee.Name
                        }
                    }
                } catch {}
            }
            if ($op.OperandType -eq [Reflection.Emit.OperandType]::InlineField) {
                $token = [BitConverter]::ToInt32($bytes, $position)
                try {
                    $field = $method.Module.ResolveField($token)
                    if ($field.Name -in $TargetFields) {
                        [pscustomobject]@{
                            CallerType = $type.FullName
                            Caller = $method.Name
                            CalleeType = $field.DeclaringType.FullName
                            Callee = $field.Name
                        }
                    }
                } catch {}
            }
            $position += $size
        }
    }
}

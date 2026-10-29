# Извлекает стандартные значки Windows (из системных DLL) в PNG — для docs/icons.gif.
param([Parameter(Mandatory = $true)][string]$OutDir, [int]$Size = 96)
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Native -Name Icons -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
[DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
'@
New-Item -ItemType Directory -Force $OutDir | Out-Null
$imageres = "$env:windir\System32\imageres.dll"
$map = [ordered]@{
    'thispc'   = -109; 'recycle' = -55; 'network' = -25; 'user' = -123; 'folder' = -3
    'downloads' = -184; 'pictures' = -113; 'drive' = -32; 'txt' = -102; 'zip' = -174
}
foreach ($name in $map.Keys) {
    $h = New-Object IntPtr[] 1; $ids = New-Object 'UInt32[]' 1
    $n = [Native.Icons]::PrivateExtractIcons($imageres, $map[$name], $Size, $Size, $h, $ids, 1, 0)
    if ($n -eq 0 -or $h[0] -eq [IntPtr]::Zero) { Write-Warning "нет значка $name"; continue }
    $bmp = ([System.Drawing.Icon]::FromHandle($h[0])).ToBitmap()
    $bmp.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose(); [Native.Icons]::DestroyIcon($h[0]) | Out-Null
}
Write-Host "Готово: $OutDir"

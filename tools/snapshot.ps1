# Снимок состояния, которое меняет IcoSwap: хэши ярлыков/desktop.ini и значения реестра.
# Использование: tools\snapshot.ps1 -Out before.txt ; потом сравнить с after-rollback.txt
param([Parameter(Mandatory = $true)][string]$Out)

$lines = New-Object System.Collections.Generic.List[string]
$roots = @(
    [Environment]::GetFolderPath('DesktopDirectory'),
    [Environment]::GetFolderPath('CommonDesktopDirectory'),
    "$env:APPDATA\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar",
    [Environment]::GetFolderPath('Programs')
)
foreach ($r in $roots) {
    Get-ChildItem $r -Recurse -Include *.lnk, *.url -Force -ErrorAction SilentlyContinue | Sort-Object FullName | ForEach-Object {
        $lines.Add("FILE $($_.FullName) $((Get-FileHash $_.FullName -Algorithm SHA256).Hash) $($_.Attributes)")
    }
}
foreach ($f in 'Desktop', 'MyDocuments', 'MyPictures', 'MyMusic', 'MyVideos') {
    $p = [Environment]::GetFolderPath($f)
    $ini = Join-Path $p 'desktop.ini'
    $h = if (Test-Path -LiteralPath $ini) { (Get-FileHash -LiteralPath $ini).Hash } else { 'none' }
    $lines.Add("DIR $p $((Get-Item -LiteralPath $p -Force).Attributes) ini=$h")
}
$dl = "$env:USERPROFILE\Downloads"
$h = if (Test-Path "$dl\desktop.ini") { (Get-FileHash "$dl\desktop.ini").Hash } else { 'none' }
$lines.Add("DIR $dl $((Get-Item $dl -Force).Attributes) ini=$h")

function Dump($path) {
    if (Test-Path $path) {
        $k = Get-Item $path
        foreach ($n in $k.GetValueNames()) { $lines.Add("REG $path [$n]=$($k.GetValue($n, $null, 'DoNotExpandEnvironmentNames'))") }
        foreach ($s in $k.GetSubKeyNames()) { Dump "$path\$s" }
    } else { $lines.Add("REG $path <missing>") }
}
Dump 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID'
Dump 'HKCU:\Software\Classes\Applications\Explorer.exe'
foreach ($p in 'AppX4ztfk9wxr86nxmzzq47px0nh0e58b8fw', 'CompressedFolder', 'Word.Document.12', 'MSEdgePDF') { Dump "HKCU:\Software\Classes\$p\DefaultIcon" }
$lines | Set-Content -Encoding UTF8 $Out
Write-Host "$($lines.Count) строк → $Out"

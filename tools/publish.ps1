# Собирает portable-версию: publish\IcoSwap\ (запускать IcoSwap.exe) и publish\IcoSwap-portable-win-x64.zip
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'publish\IcoSwap'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish (Join-Path $root 'src\IcoSwap.App\IcoSwap.App.csproj') -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { throw 'Сборка IcoSwap.App не удалась' }
dotnet publish (Join-Path $root 'src\IcoSwap.Cli\IcoSwap.Cli.csproj') -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { throw 'Сборка IcoSwap.Cli не удалась' }

Copy-Item (Join-Path $root 'README.md') $out
Copy-Item -Recurse (Join-Path $root 'samples\TestPack') (Join-Path $out 'TestPack')
Get-ChildItem $out -Filter *.pdb -Recurse | Remove-Item

$zip = Join-Path $root 'publish\IcoSwap-portable-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $out -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Готово: $out`n        $zip"

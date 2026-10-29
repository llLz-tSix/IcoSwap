# Генерирует иконку приложения и тестовый пак (PNG/SVG/ICO), используя System.Drawing.
# Запуск: powershell -ExecutionPolicy Bypass -File tools\make-assets.ps1 -Cli <путь к icoswap-cli.exe>
param([Parameter(Mandatory = $true)][string]$Cli)

Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot

function New-IconPng([string]$path, [string]$text, [System.Drawing.Color]$bg, [string]$shape = 'round', [int]$size = 256) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.Clear([System.Drawing.Color]::Transparent)
    $m = [int]($size * 0.06); $w = $size - 2 * $m
    $top = [System.Drawing.Color]::FromArgb(255, [Math]::Min(255, $bg.R + 50), [Math]::Min(255, $bg.G + 50), [Math]::Min(255, $bg.B + 50))
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle $m, $m, $w, $w), $top, $bg, 90
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    switch ($shape) {
        'circle' { $gp.AddEllipse($m, $m, $w, $w) }
        'folder' {
            $gp.AddRectangle((New-Object System.Drawing.Rectangle $m, ([int]($size * 0.28)), $w, ([int]($size * 0.62))))
            $gp.AddRectangle((New-Object System.Drawing.Rectangle $m, ([int]($size * 0.18)), ([int]($w * 0.45)), ([int]($size * 0.14))))
        }
        'drive' { $gp.AddRectangle((New-Object System.Drawing.Rectangle $m, ([int]($size * 0.3)), $w, ([int]($size * 0.4)))) }
        'doc' {
            $gp.AddRectangle((New-Object System.Drawing.Rectangle ([int]($size * 0.18)), $m, ([int]($size * 0.64)), $w))
        }
        default {
            $r = [int]($size * 0.22)
            $gp.AddArc($m, $m, $r, $r, 180, 90); $gp.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
            $gp.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90); $gp.AddArc($m, $m + $w - $r, $r, $r, 90, 90); $gp.CloseFigure()
        }
    }
    $g.FillPath($brush, $gp)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 0, 0, 0)), ([float]($size / 64))
    $g.DrawPath($pen, $gp)
    $fontSize = if ($text.Length -le 2) { $size * 0.42 } elseif ($text.Length -le 3) { $size * 0.30 } else { $size * 0.22 }
    $font = New-Object System.Drawing.Font 'Segoe UI', ([float]$fontSize), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $rect = New-Object System.Drawing.RectangleF 0, ([float]($size * 0.04)), $size, $size
    $g.DrawString($text, $font, [System.Drawing.Brushes]::White, $rect, $fmt)
    $g.Dispose(); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

# --- иконка приложения ---
$assets = Join-Path $root 'src\IcoSwap.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
# иконка приложения рисуется вручную в Assets\IcoSwap.svg (знак переработки с черепом)
& $Cli convert (Join-Path $assets 'IcoSwap.svg') (Join-Path $assets 'IcoSwap.ico') | Out-Null

# --- тестовый пак ---
$pack = Join-Path $root 'samples\TestPack'
if (Test-Path $pack) { Remove-Item -Recurse -Force $pack }
New-Item -ItemType Directory -Force $pack, (Join-Path $pack 'drives'), (Join-Path $pack 'filetypes') | Out-Null
$c = { param($r, $g, $b) [System.Drawing.Color]::FromArgb(255, $r, $g, $b) }

New-IconPng (Join-Path $pack 'this-pc.png') 'PC' (& $c 40 90 200)
New-IconPng (Join-Path $pack 'recycle-bin-empty.png') 'R0' (& $c 60 140 90) 'circle'
New-IconPng (Join-Path $pack 'recycle-bin-full.png') 'R1' (& $c 200 70 50) 'circle'
New-IconPng (Join-Path $pack 'network.png') 'NET' (& $c 20 140 160)
New-IconPng (Join-Path $pack 'user.png') 'USR' (& $c 120 80 170)
New-IconPng (Join-Path $pack 'control-panel.png') 'CP' (& $c 90 90 90)
New-IconPng (Join-Path $pack 'documents.png') 'DOC' (& $c 230 160 30) 'folder'
New-IconPng (Join-Path $pack 'Загрузки.png') 'DL' (& $c 40 160 70) 'folder'
New-IconPng (Join-Path $pack 'pictures.png') 'PIC' (& $c 210 60 140) 'folder'
New-IconPng (Join-Path $pack 'folder.png') 'F' (& $c 240 190 40) 'folder'
New-IconPng (Join-Path $pack 'drives\c.png') 'C:' (& $c 70 70 80) 'drive'
New-IconPng (Join-Path $pack 'drive.png') 'HD' (& $c 110 110 130) 'drive'
New-IconPng (Join-Path $pack 'filetypes\txt.png') 'TXT' (& $c 80 110 140) 'doc'
New-IconPng (Join-Path $pack 'zip.png') 'ZIP' (& $c 150 110 40) 'doc'
New-IconPng (Join-Path $pack 'docx.png') 'W' (& $c 30 80 170) 'doc'
New-IconPng (Join-Path $pack 'spotify.png') 'SP' (& $c 30 185 84) 'circle'
New-IconPng (Join-Path $pack 'discord.png') 'DC' (& $c 88 101 242)
New-IconPng (Join-Path $pack 'file-explorer.png') 'EX' (& $c 250 200 60)
New-IconPng (Join-Path $pack 'zz-unknown-thing.png') '?' (& $c 128 128 128) 'circle'

# одна иконка сразу в .ico, одна в SVG
New-IconPng (Join-Path $env:TEMP 'icoswap_blender.png') 'BL' (& $c 234 118 0)
& $Cli convert (Join-Path $env:TEMP 'icoswap_blender.png') (Join-Path $pack 'blender.ico') | Out-Null
Remove-Item (Join-Path $env:TEMP 'icoswap_blender.png')
@'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
  <defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3fa9f5"/><stop offset="1" stop-color="#0065a9"/></linearGradient></defs>
  <rect x="4" y="4" width="56" height="56" rx="12" fill="url(#g)"/>
  <path d="M44 14 L24 32 L44 50 L50 46 L34 32 L50 18 Z" fill="#fff"/>
  <path d="M14 26 L20 32 L14 38 Z" fill="#fff"/>
</svg>
'@ | Set-Content -Encoding UTF8 (Join-Path $pack 'vscode.svg')

@'
{
  "name": "IcoSwap Test Pack",
  "author": "IcoSwap",
  "map": {
    "user.png": "system:userfiles",
    "control-panel.png": "system:controlpanel"
  },
  "ignore": []
}
'@ | Set-Content -Encoding UTF8 (Join-Path $pack 'manifest.json')

Write-Host "Готово: $assets\IcoSwap.ico и $pack"

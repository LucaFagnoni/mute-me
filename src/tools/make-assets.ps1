# Rigenera le blob BGRA incorporate in MuteMe.exe a partire dai PNG in src/art.
#
# Non gira durante la build: le blob sono versionate, cosi' compilare richiede
# solo MSVC. Rilancia questo script solo se cambi le immagini sorgente.
#
#   powershell -ExecutionPolicy Bypass -File src/tools/make-assets.ps1
#
# Le due destinazioni vogliono convenzioni di alpha diverse:
#   overlay_*.bgra  64x64  premoltiplicato   -> UpdateLayeredWindow con AC_SRC_ALPHA
#   tray_*.bgra     32x32  alpha diretto     -> CreateIconIndirect

Add-Type -AssemblyName System.Drawing

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$art  = Join-Path $root "src\art"
$out  = Join-Path $root "src\assets"

function Export-Bgra {
    param(
        [string] $SourcePng,
        [int]    $Size,
        [string] $Destination,
        [bool]   $Premultiply
    )

    $img = [System.Drawing.Image]::FromFile($SourcePng)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($img, 0, 0, $Size, $Size)
    $g.Dispose()

    $rect = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($Size * $Size * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)

    if ($Premultiply) {
        for ($i = 0; $i -lt $bytes.Length; $i += 4) {
            $a = $bytes[$i + 3]
            $bytes[$i]     = [byte](($bytes[$i]     * $a) / 255)
            $bytes[$i + 1] = [byte](($bytes[$i + 1] * $a) / 255)
            $bytes[$i + 2] = [byte](($bytes[$i + 2] * $a) / 255)
        }
    }

    [System.IO.File]::WriteAllBytes($Destination, $bytes)
    $bmp.Dispose()
    $img.Dispose()
    "{0,-22} {1,2}x{1,-2} premoltiplicato={2,-5} {3,6} byte" -f `
        (Split-Path $Destination -Leaf), $Size, $Premultiply, $bytes.Length
}

foreach ($state in @("muted", "unmuted")) {
    Export-Bgra "$art\$state.png" 64 "$out\overlay_$state.bgra" $true
    Export-Bgra "$art\$state.png" 32 "$out\tray_$state.bgra"    $false
}

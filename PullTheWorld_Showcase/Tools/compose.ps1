# Pull The World showcase - image compositions (hero images and deck slides).
# System.Drawing only, Poppins loaded from the project's TTFs so no font install is needed.
param([string]$What = "all")
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Split-Path -Parent $Root
$Stills = "$Root\RawCaptures\stills"
$Work = "$Root\RawCaptures\_work"
$Hero = "$Root\HeroImages"
$SlidesDir = "$Root\Presentation\Slides"
New-Item -ItemType Directory -Force -Path $Hero, $SlidesDir | Out-Null

$pfc = New-Object System.Drawing.Text.PrivateFontCollection
$pfc.AddFontFile("$Project\Assets\PullTheWorld\Art\Fonts\Poppins-Bold.ttf")
$pfc.AddFontFile("$Project\Assets\PullTheWorld\Art\Fonts\Poppins-SemiBold.ttf")
function Fam($name) { foreach ($f in $pfc.Families) { if ($f.Name -eq $name) { return $f } }; return $pfc.Families[0] }
$FamBold = Fam "Poppins"; $FamSemi = Fam "Poppins SemiBold"
function FontBold([single]$size) { [System.Drawing.Font]::new($FamBold, $size, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel) }
function FontSemi([single]$size) { try { [System.Drawing.Font]::new($FamSemi, $size, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel) } catch { [System.Drawing.Font]::new($FamBold, $size, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel) } }

$Ink = [System.Drawing.Color]::FromArgb(255, 70, 89, 79)         # the game's settings ink
$Sage = [System.Drawing.Color]::FromArgb(255, 92, 112, 97)
$Cream = [System.Drawing.Color]::FromArgb(255, 253, 241, 221)
$Blush = [System.Drawing.Color]::FromArgb(255, 246, 225, 228)
$Shadow = [System.Drawing.Color]::FromArgb(120, 98, 121, 102)

function NewCanvas($w, $h, $bg) { $b = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($b); $g.Clear($bg); $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.PixelOffsetMode = 'HighQuality'; return @($b, $g) }
function Img($path) { return [System.Drawing.Bitmap]::FromFile($path) }
function DrawFit($g, $img, $x, $y, $w, $h, $biasY = 0.5) {
  # cover-fit: fill the rect, crop the overflow; biasY 0 keeps the top, 1 the bottom, 0.5 centres
  $sc = [math]::Max($w / $img.Width, $h / $img.Height); $sw = $img.Width * $sc; $sh = $img.Height * $sc
  $g.SetClip([System.Drawing.Rectangle]::new($x, $y, $w, $h)); $g.DrawImage($img, [single]($x + ($w - $sw) / 2), [single]($y + ($h - $sh) * $biasY), [single]$sw, [single]$sh); $g.ResetClip()
}
function DrawInside($g, $img, $x, $y, $w, $h) {
  $sc = [math]::Min($w / $img.Width, $h / $img.Height); $sw = $img.Width * $sc; $sh = $img.Height * $sc
  $g.DrawImage($img, [single]($x + ($w - $sw) / 2), [single]($y + ($h - $sh) / 2), [single]$sw, [single]$sh)
}
function RoundRect($x, $y, $w, $h, $r) { $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $p.AddArc($x, $y, 2*$r, 2*$r, 180, 90); $p.AddArc($x+$w-2*$r, $y, 2*$r, 2*$r, 270, 90); $p.AddArc($x+$w-2*$r, $y+$h-2*$r, 2*$r, 2*$r, 0, 90); $p.AddArc($x, $y+$h-2*$r, 2*$r, 2*$r, 90, 90); $p.CloseFigure(); return $p }
function DrawCard($g, $img, $x, $y, $w, $h, $r, $biasY = 0.5) {
  # a rounded "phone" card with a soft shadow, image cover-fitted inside
  $sh = RoundRect ($x + 6) ($y + 14) $w $h $r; $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 70, 60, 80))), $sh)
  $path = RoundRect $x $y $w $h $r; $g.SetClip($path); DrawFit $g $img $x $y $w $h $biasY; $g.ResetClip()
  $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), 2), $path)
}
function Text($g, $s, $font, $color, $x, $y, $align = "left", $tracking = 0) {
  $brush = New-Object System.Drawing.SolidBrush $color
  if ($tracking -eq 0) {
    $sz = $g.MeasureString($s, $font); $dx = 0; if ($align -eq "center") { $dx = -$sz.Width / 2 } elseif ($align -eq "right") { $dx = -$sz.Width }
    $g.DrawString($s, $font, $brush, [single]($x + $dx), [single]$y); return $sz.Width
  }
  $fmt = [System.Drawing.StringFormat]::GenericTypographic
  $total = 0; $ws = @(); foreach ($ch in $s.ToCharArray()) { $w = $g.MeasureString([string]$ch, $font, 10000, $fmt).Width; if ($ch -eq " ") { $w = [math]::Max($w, $font.Size * 0.32) }; $ws += $w; $total += $w + $tracking }
  $total -= $tracking; $cx = $x; if ($align -eq "center") { $cx = $x - $total / 2 } elseif ($align -eq "right") { $cx = $x - $total }
  for ($i = 0; $i -lt $s.Length; $i++) { $g.DrawString([string]$s[$i], $font, $brush, [single]$cx, [single]$y, $fmt); $cx += $ws[$i] + $tracking }
  return $total
}
function TextShadow($g, $s, $font, $color, $x, $y, $align = "left", $tracking = 0) {
  Text $g $s $font ([System.Drawing.Color]::FromArgb(70, 40, 50, 45)) ($x + 2) ($y + 3) $align $tracking | Out-Null
  return (Text $g $s $font $color $x $y $align $tracking)
}
function Logo($g, $cx, $cy, $w) { $l = Img "$Project\Assets\PullTheWorld\Art\UI\Layer 9.png"; $h = $w * $l.Height / $l.Width; $g.DrawImage($l, [single]($cx - $w / 2), [single]($cy - $h / 2), [single]$w, [single]$h); $l.Dispose(); return $h }
function Save($b, $path) { $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); "wrote $path" }

$Tag1 = "ROTATE THE WORLD.  GRAVITY DOES THE REST."
$Studio = "OBSCURE GAMES"

if ($What -eq "all" -or $What -eq "hero") {
  # ---- Hero 1: presentation cover, 1920x1080 - the world above, the title on the clouds below
  $c = NewCanvas 1920 1080 $Blush; $b = $c[0]; $g = $c[1]
  $img = Img "$Stills\hero_l01_wide_landscape.png"; DrawFit $g $img 0 0 1920 1080; $img.Dispose()
  Logo $g 960 700 640 | Out-Null
  TextShadow $g $Tag1 (FontSemi 30) $Ink 960 862 "center" 5 | Out-Null
  Save $b "$Hero\Hero_Cover_1920x1080.png"; $g.Dispose(); $b.Dispose()

  # ---- Hero 2: landscape promo, 2560x1440 - the pull, close and tilted, title top-left
  $c = NewCanvas 2560 1440 $Blush; $b = $c[0]; $g = $c[1]
  $img = Img "$Stills\hero_l01_tilted_landscape.png"; DrawFit $g $img 0 0 2560 1440; $img.Dispose()
  Logo $g 430 225 600 | Out-Null
  TextShadow $g $Tag1 (FontSemi 30) $Ink 430 395 "center" 5 | Out-Null
  Save $b "$Hero\Hero_Landscape_2560x1440.png"; $g.Dispose(); $b.Dispose()

  # ---- Hero 3: vertical / mobile promo, 1440x2560 - the wide world, title in the lower third
  $c = NewCanvas 1440 2560 $Blush; $b = $c[0]; $g = $c[1]
  $img = Img "$Stills\l01_wide.png"; DrawFit $g $img 0 0 1440 2560; $img.Dispose()
  Logo $g 720 1950 1060 | Out-Null
  TextShadow $g "ROTATE THE WORLD." (FontSemi 40) $Ink 720 2250 "center" 7 | Out-Null
  TextShadow $g "GRAVITY DOES THE REST." (FontSemi 40) $Ink 720 2312 "center" 7 | Out-Null
  Save $b "$Hero\Hero_Vertical_1440x2560.png"; $g.Dispose(); $b.Dispose()
}

# =============================================================================== slides ======
function SlideBase($title, $n) {
  $c = NewCanvas 1920 1080 $Blush; $b = $c[0]; $g = $c[1]
  # a whisper of the sky gradient so the blush is not flat
  $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new(0, 1080)), ([System.Drawing.Color]::FromArgb(255, 252, 226, 216)), ([System.Drawing.Color]::FromArgb(255, 241, 227, 234))
  $g.FillRectangle($lg, 0, 0, 1920, 1080)
  if ($title) { TextShadow $g $title (FontBold 56) $Ink 110 78 "left" 1 | Out-Null }
  Text $g ("PULL THE WORLD   " + $n.ToString("00")) (FontSemi 20) $Sage 110 1036 "left" 3 | Out-Null
  return @($b, $g)
}
function Caption($g, $s, $cx, $y) { Text $g $s (FontSemi 22) $Sage $cx $y "center" 4 | Out-Null }
function Para($g, $s, $font, $color, $x, $y, $maxW) {
  $fmt = New-Object System.Drawing.StringFormat; $rect = [System.Drawing.RectangleF]::new($x, $y, $maxW, 600)
  $g.DrawString($s, $font, (New-Object System.Drawing.SolidBrush $color), $rect, $fmt)
}
function Frame($shot, $n) { return "$Root\RawCaptures\clips\$shot\f_$($n.ToString('0000')).jpg" }

if ($What -eq "all" -or $What -eq "slides") {
  # 01 - cover
  $c = NewCanvas 1920 1080 $Blush; $b = $c[0]; $g = $c[1]
  $img = Img "$Hero\Hero_Cover_1920x1080.png"; $g.DrawImage($img, 0, 0, 1920, 1080); $img.Dispose()
  Save $b "$SlidesDir\slide_01_cover.png"; $g.Dispose(); $b.Dispose()

  # 02 - the idea
  $c = SlideBase "The idea" 2; $b = $c[0]; $g = $c[1]
  TextShadow $g "Hold a small world" (FontBold 74) $Ink 110 300 | Out-Null
  TextShadow $g "in your hands." (FontBold 74) $Ink 110 392 | Out-Null
  Para $g "You never move the character. You pull the whole island, and gravity carries a glowing orb home." (FontSemi 34) $Sage 112 540 900
  $img = Img "$Stills\l01_tilted_close.png"; DrawCard $g $img 1290 95 500 890 34; $img.Dispose()
  Save $b "$SlidesDir\slide_02_idea.png"; $g.Dispose(); $b.Dispose()

  # 03 - core mechanic: pull, roll, arrive
  $c = SlideBase "Core mechanic" 3; $b = $c[0]; $g = $c[1]
  Text $g "ONE GESTURE. DRAG ANYWHERE TO ROTATE THE ISLAND. GRAVITY DOES THE REST." (FontSemi 24) $Sage 110 160 "left" 3 | Out-Null
  $frames = @(12, 42, 66); $caps = @("1   PULL", "2   ROLL", "3   ARRIVE"); $xs = @(190, 740, 1290)
  for ($i = 0; $i -lt 3; $i++) { $img = Img (Frame "04_mechanic_travel" $frames[$i]); DrawCard $g $img $xs[$i] 235 440 700 30; $img.Dispose(); Caption $g $caps[$i] ($xs[$i] + 220) 962 }
  Save $b "$SlidesDir\slide_03_mechanic.png"; $g.Dispose(); $b.Dispose()

  # 04 - gameplay: five situations
  $c = SlideBase "Gameplay" 4; $b = $c[0]; $g = $c[1]
  Text $g "A FEW READABLE PIECES, COMBINED FIFTY WAYS." (FontSemi 24) $Sage 110 160 "left" 3 | Out-Null
  $shots = @("l03_spikes", "l07_gem", "l19_water", "l22_rock", "l29_enemies"); $caps = @("SPIKES", "GEMS", "WATER", "BOULDERS", "URCHINS")
  for ($i = 0; $i -lt 5; $i++) { $x = 110 + $i * 345; $img = Img "$Stills\$($shots[$i]).png"; DrawCard $g $img $x 235 320 690 26; $img.Dispose(); Caption $g $caps[$i] ($x + 160) 952 }
  Save $b "$SlidesDir\slide_04_gameplay.png"; $g.Dispose(); $b.Dispose()

  # 05 - world & progression: the push
  $c = SlideBase "World and progression" 5; $b = $c[0]; $g = $c[1]
  Text $g "FINISH AN ISLAND AND THE CAMERA PUSHES FORWARD. THE NEXT LEVEL WAS STANDING IN THE SKY THE WHOLE TIME." (FontSemi 24) $Sage 110 160 "left" 3 | Out-Null
  $frames = @(96, 114, 150); $caps = @("THE DOOR OPENS", "THE CAMERA TRAVELS", "THE NEXT ISLAND IS LIVE"); $xs = @(190, 740, 1290)
  for ($i = 0; $i -lt 3; $i++) { $img = Img (Frame "04_mechanic_travel" $frames[$i]); DrawCard $g $img $xs[$i] 235 440 700 30; $img.Dispose(); Caption $g $caps[$i] ($xs[$i] + 220) 962 }
  Save $b "$SlidesDir\slide_05_world.png"; $g.Dispose(); $b.Dispose()

  # 06 - visual identity: the painted world and its details
  $c = SlideBase "Visual identity" 6; $b = $c[0]; $g = $c[1]
  $img = Img "$Stills\hero_l35_golden_landscape.png"; DrawCard $g $img 110 175 1700 560 30 0.72; $img.Dispose()
  $src = Img "$Stills\hero_l01_tilted_landscape.png"
  $crops = @(@(2500, 330, 940, 530), @(1480, 200, 940, 530), @(2200, 780, 940, 530)); $caps = @("A GLOWING DOOR HOME", "A GLASS ORB WITH A STAR INSIDE", "GRASS, VINES, FLOWERS, LIGHT")
  for ($i = 0; $i -lt 3; $i++) {
    $r = $crops[$i]; $piece = $src.Clone([System.Drawing.Rectangle]::new($r[0], $r[1], $r[2], $r[3]), $src.PixelFormat)
    $x = 110 + $i * 580; DrawCard $g $piece $x 770 540 200 22; $piece.Dispose(); Caption $g $caps[$i] ($x + 270) 990
  }
  $src.Dispose()
  Save $b "$SlidesDir\slide_06_visual.png"; $g.Dispose(); $b.Dispose()

  # 07 - the experience
  $c = SlideBase "The experience" 7; $b = $c[0]; $g = $c[1]
  $heads = @("Understood in one second", "Physics you can feel", "A world you travel through", "One idea per level")
  $subs = @("Drag anywhere. The world turns. That is the whole tutorial.", "Weight, momentum and a settling wobble in every pull.", "No loading screens, no cuts: the camera pushes on to the next island.", "Fifty handcrafted islands across three skies, each teaching one thing.")
  for ($i = 0; $i -lt 4; $i++) { $y = 205 + $i * 190; TextShadow $g $heads[$i] (FontBold 44) $Ink 110 $y | Out-Null; Para $g $subs[$i] (FontSemi 26) $Sage 112 ($y + 64) 1000 }
  $img = Img "$Stills\l01_wide.png"; DrawCard $g $img 1290 95 500 890 34; $img.Dispose()
  Save $b "$SlidesDir\slide_07_experience.png"; $g.Dispose(); $b.Dispose()

  # 08 - closing hero
  $c = NewCanvas 1920 1080 $Blush; $b = $c[0]; $g = $c[1]
  $img = Img "$Hero\Hero_Landscape_2560x1440.png"; $g.DrawImage($img, 0, 0, 1920, 1080); $img.Dispose()
  Save $b "$SlidesDir\slide_08_closing.png"; $g.Dispose(); $b.Dispose()
}

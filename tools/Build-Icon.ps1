param([string]$AssetDirectory = (Join-Path $PSScriptRoot '..\ScanControl.App\Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$assetRoot = [IO.Path]::GetFullPath($AssetDirectory)
$source = [IO.File]::ReadAllText((Join-Path $assetRoot 'ScanControl.xaml'))
function Render-Png([int]$size) {
    $visual = [Windows.Markup.XamlReader]::Parse($source)
    $visual.Width = $size; $visual.Height = $size
    $visual.Measure([Windows.Size]::new($size, $size))
    $visual.Arrange([Windows.Rect]::new(0, 0, $size, $size)); $visual.UpdateLayout()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [IO.MemoryStream]::new()
    try { $encoder.Save($memory); return ,$memory.ToArray() } finally { $memory.Dispose() }
}
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @($sizes | ForEach-Object { ,(Render-Png $_) })
$stream = [IO.File]::Create((Join-Path $assetRoot 'ScanControl.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Length)
    $offset = 6 + 16 * $sizes.Length
    for ($i = 0; $i -lt $sizes.Length; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $stream.Dispose() }
[IO.File]::WriteAllBytes((Join-Path $assetRoot 'ScanControl.png'), (Render-Png 256))
[IO.File]::WriteAllBytes((Join-Path $assetRoot 'ScanControl-preview.png'), (Render-Png 1024))
Write-Output "Created ScanControl.ico (7 sizes), ScanControl.png and ScanControl-preview.png"

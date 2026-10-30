param([string]$DependencyArchive = '', [switch]$SkipTests, [switch]$Package)
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-','').ToLowerInvariant() }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
$projectRoot = $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $projectRoot 'dependencies.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$packageDir = Join-Path $projectRoot 'packages'
$version = [regex]::Match([IO.File]::ReadAllText((Join-Path $projectRoot 'src\AssemblyInfo.cs')),'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)').Groups[1].Value
if (!$version) { throw 'Assembly version not found.' }
$outputDir = Join-Path $projectRoot ('artifacts\QuietMonitor-v'+$version)
New-Item -ItemType Directory -Force -Path $packageDir,$outputDir | Out-Null
if ($DependencyArchive) { $archive = (Resolve-Path -LiteralPath $DependencyArchive).Path }
else {
    $archive = Join-Path $packageDir 'LibreHardwareMonitor.zip'
    if (!(Test-Path -LiteralPath $archive)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $lock.url -OutFile $archive -UseBasicParsing
    }
}
if ((Get-Sha256 $archive) -ne $lock.sha256) { throw 'Dependency archive SHA-256 mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($item in $lock.files) {
        $entry = $zip.GetEntry($item.name)
        if (!$entry) { throw "Missing dependency: $($item.name)" }
        $destination = Join-Path $outputDir $item.name
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$true)
        if ((Get-Sha256 $destination) -ne $item.sha256) { throw "Dependency hash mismatch: $($item.name)" }
    }
} finally { $zip.Dispose() }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x x64 compiler not found. Install .NET Framework 4.8.' }
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object Drawing.Bitmap 32,32
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.Clear([Drawing.Color]::FromArgb(17,21,28))
    $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(88,214,176)),3
    try { $points = [Drawing.Point[]]@((New-Object Drawing.Point 3,20),(New-Object Drawing.Point 9,20),(New-Object Drawing.Point 13,7),(New-Object Drawing.Point 19,25),(New-Object Drawing.Point 23,15),(New-Object Drawing.Point 29,15)); $graphics.DrawLines($pen,$points) } finally { $pen.Dispose() }
    $iconPath = Join-Path $packageDir 'app.ico'
    # ICO containing PNG: no unmanaged icon handles are allocated.
    $png = New-Object IO.MemoryStream
    try {
        $bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)
        $stream = [IO.File]::Create($iconPath); $writer = New-Object IO.BinaryWriter $stream
        try { $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]1);$writer.Write([byte]32);$writer.Write([byte]32);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$png.Length);$writer.Write([uint32]22);$writer.Write($png.ToArray()) } finally { $writer.Dispose();$stream.Dispose() }
    } finally { $png.Dispose() }
} finally { $graphics.Dispose();$bitmap.Dispose() }
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/debug-','/codepage:65001',('/out:'+(Join-Path $outputDir 'QuietMonitor.exe')),('/win32manifest:'+(Join-Path $projectRoot 'src\app.manifest')),('/win32icon:'+$iconPath),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll','/reference:System.Management.dll',('/reference:'+(Join-Path $outputDir 'LibreHardwareMonitorLib.dll')),('/reference:'+(Join-Path $outputDir 'DiskInfoToolkit.dll')))
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\QuietMonitor.exe.config') -Destination $outputDir -Force
if (!$SkipTests) {
    $testExe = Join-Path $packageDir 'CoreTests.exe'
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /debug- /codepage:65001 "/out:$testExe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (Join-Path $projectRoot 'src\Model.cs') (Join-Path $projectRoot 'src\Settings.cs') (Join-Path $projectRoot 'src\OverlayPainter.cs') (Join-Path $projectRoot 'src\BreakReminder.cs') (Join-Path $projectRoot 'tests\CoreTests.cs') (Join-Path $projectRoot 'tests\BreakReminderTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
}
if ($Package) {
    # Release inputs are explicit; never zip the project root or an existing build directory wholesale.
    $files = @('QuietMonitor.exe','QuietMonitor.exe.config') + @($lock.files | ForEach-Object { $_.name })
    $release = Join-Path $projectRoot ('artifacts\QuietMonitor-v'+$version+'-win-x64.zip')
    if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release }
    $archiveOut = [IO.Compression.ZipFile]::Open($release,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archiveOut,(Join-Path $outputDir $file),('QuietMonitor/'+$file),[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
        foreach ($file in @('README.md','LICENSE','THIRD_PARTY.md','dependencies.lock.json')) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archiveOut,(Join-Path $projectRoot $file),('QuietMonitor/'+$file),[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
        foreach ($file in @('NOTICE','COMMERCIAL_LICENSE.md','SECURITY.md','docs/VALIDATION.md')) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archiveOut,(Join-Path $projectRoot $file),('QuietMonitor/'+$file),[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
        foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'third-party') -File)) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archiveOut,$file.FullName,('QuietMonitor/third-party/'+$file.Name),[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
    } finally { $archiveOut.Dispose() }
    $digest = Get-Sha256 $release
    [IO.File]::WriteAllText(($release+'.sha256'),($digest+'  '+[IO.Path]::GetFileName($release)+[Environment]::NewLine),(New-Object Text.UTF8Encoding $false))
    Write-Host ('Package: '+$release)
}
Write-Host ('Built: '+(Join-Path $outputDir 'QuietMonitor.exe'))

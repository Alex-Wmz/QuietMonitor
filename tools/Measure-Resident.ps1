param(
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$Scene,
    [int]$MonitorPid = 0,
    [int]$GamePid = 0,
    [ValidateRange(5,1800)][int]$Seconds = 180,
    [ValidateRange(0,60)][int]$WarmupSeconds = 15
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolDir = Join-Path $root 'local\measurement-tool'
[IO.Directory]::CreateDirectory($toolDir) | Out-Null
$exe = Join-Path $toolDir 'ResidentMeasure.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /debug- "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'ResidentMeasure.cs')
if ($LASTEXITCODE -ne 0) { throw 'Measurement helper build failed.' }
& $exe $Seconds $OutputPath $Scene $MonitorPid $GamePid $WarmupSeconds
if ($LASTEXITCODE -ne 0) { throw 'Measurement did not complete.' }

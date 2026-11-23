[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $PSScriptRoot 'src\WatchDogs2HybridCpuInstaller.cs'
$database = Join-Path $PSScriptRoot 'compatibility\WatchDogs2-HybridCPU.sdb'
$outputDirectory = Join-Path $PSScriptRoot 'dist'
$installer = Join-Path $outputDirectory 'WatchDogs2-HybridCPU-Setup.exe'

foreach ($file in @($compiler, $source, $database)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required build file was not found at $file"
    }
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$compilerArgs = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    "/resource:$database,WatchDogs2.sdb", "/out:$installer", $source
)
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "The installer build failed with exit code $LASTEXITCODE."
}
Get-Item -LiteralPath $installer | Select-Object FullName, Length

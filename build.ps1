$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    $argsList = @('build', 'AdaptiveText.csproj', '-c', 'Release', '--nologo', '-p:RestoreLockedMode=true')
    & dotnet @argsList
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $output = Join-Path $PSScriptRoot 'bin/Release/net8.0'
    $package = Join-Path $PSScriptRoot 'InkWhite-0.1.7-ci2.1.0.1.cipx'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($output, $package)
    Write-Host "Created $package"
} finally { Pop-Location }










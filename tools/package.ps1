<#
Build a Release and zip it for a GitHub release: dist\BYEFrameBlink.zip
  package.ps1
The zip holds BYEFrameBlink.dll, README.md, LICENSE and THIRD_PARTY_NOTICES.md; users extract it into <BepInEx>\plugins\BYEFrameBlink\.
#>
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot
Push-Location $Root
try {
    dotnet build -c Release -p:SkipDeploy=true | Select-String ' error |Build succeeded'
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
    $dist = Join-Path $Root 'dist'
    $stage = Join-Path $dist 'BYEFrameBlink'
    Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory $stage | Out-Null
    Copy-Item (Join-Path $Root 'bin\Release\net46\BYEFrameBlink.dll') $stage
    Copy-Item (Join-Path $Root 'README.md') $stage
    foreach ($f in 'LICENSE', 'THIRD_PARTY_NOTICES.md') { Copy-Item (Join-Path $Root $f) $stage }
    $zip = Join-Path $dist 'BYEFrameBlink.zip'
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Remove-Item $stage -Recurse -Force
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    "zip:    $zip"
    "sha256: $hash"
} finally { Pop-Location }

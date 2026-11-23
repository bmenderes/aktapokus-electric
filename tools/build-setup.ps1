param(
    [switch]$IncludeOllama,
    [string]$OllamaInstallerPath = ""
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$distApp = Join-Path $repoRoot "dist\app"
$distRoot = Join-Path $repoRoot "dist"
$payloadDir = Join-Path $PSScriptRoot "Ake.Setup\payload"
$appZip = Join-Path $payloadDir "app.zip"
$ollamaTarget = Join-Path $payloadDir "OllamaSetup.exe"

Write-Host "==> 1/4 Ake.Editor (.exe) yayinlaniyor..."
if (Test-Path $distApp) { Remove-Item -Recurse -Force $distApp }
New-Item -ItemType Directory -Force -Path $distApp | Out-Null

dotnet publish (Join-Path $repoRoot "src\Ake.Editor\Ake.Editor.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:NuGetAudit=false `
    -o $distApp -m:1 --disable-build-servers

Write-Host "==> 2/4 ake.exe (CLI) yayinlaniyor..."
dotnet publish (Join-Path $repoRoot "src\Ake.Cli\Ake.Cli.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:NuGetAudit=false `
    -o $distApp -m:1 --disable-build-servers

Copy-Item (Join-Path $repoRoot "LICENSE") -Destination $distApp -Force
$aiDoc = Join-Path $repoRoot "docs\yapay-zeka.md"
if (Test-Path $aiDoc) { Copy-Item $aiDoc -Destination $distApp -Force }
$helpHtml = Join-Path $repoRoot "docs\yardim.html"
if (Test-Path $helpHtml) {
    Copy-Item $helpHtml -Destination $distApp -Force
    Copy-Item $helpHtml -Destination $distRoot -Force
}
Get-ChildItem $distApp -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "==> 3/4 Kurulum paketi (app.zip) hazirlaniyor..."
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
if (Test-Path $appZip) { Remove-Item -Force $appZip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($distApp, $appZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

if ($OllamaInstallerPath -ne "") {
    if (-not (Test-Path $OllamaInstallerPath)) {
        throw "Belirtilen Ollama kurulum dosyasi bulunamadi: $OllamaInstallerPath"
    }
    Copy-Item $OllamaInstallerPath -Destination $ollamaTarget -Force
    Write-Host "    OllamaSetup.exe gomulu olarak eklendi."
} elseif ($IncludeOllama) {
    Write-Host "    OllamaSetup.exe indiriliyor (https://ollama.com/download/OllamaSetup.exe)..."
    Invoke-WebRequest -Uri "https://ollama.com/download/OllamaSetup.exe" -OutFile $ollamaTarget
    Write-Host "    OllamaSetup.exe gomulu olarak indirildi."
} elseif (Test-Path $ollamaTarget) {
    Remove-Item -Force $ollamaTarget
}

Write-Host "==> 4/4 aktapokus-electric-setup.exe olusturuluyor..."
dotnet publish (Join-Path $PSScriptRoot "Ake.Setup\Ake.Setup.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:NuGetAudit=false `
    -o $distRoot -m:1 --disable-build-servers
if ($LASTEXITCODE -ne 0) { throw "Ake.Setup publish basarisiz oldu (cikis kodu: $LASTEXITCODE)." }
Get-ChildItem $distRoot -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

$setupExe = Join-Path $distRoot "aktapokus-electric-setup.exe"
$editorExe = Join-Path $distApp "Ake.Editor.exe"
$cliExe = Join-Path $distApp "ake.exe"

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$hashLines = foreach ($item in @($setupExe, $editorExe, $cliExe)) {
    if (Test-Path $item) {
        $rel = $item.Substring($distRoot.Length).TrimStart('\')
        $hash = (Get-FileHash -Path $item -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash *$rel"
    }
}
[System.IO.File]::WriteAllLines((Join-Path $distRoot "SHA256SUMS.txt"), $hashLines, $utf8NoBom)

$guide = @"
aktapokus-electric - Kolay Kurulum Rehberi (Windows 10 / 11)
============================================================

1. KURULUM (TEK TIKLA)
   - Baska bir bilgisayara yalnizca "aktapokus-electric-setup.exe" dosyasini kopyalayin.
   - Python, .NET SDK veya yonetici (Administrator) izni gerekmez.
   - Dosyaya cift tiklayin ve acilan pencerede "Hizli Kur" dugmesine basin.
   - Masaustu kisayolu ve Baslat Menusu kisayolu otomatik olusturulur.

2. OLLAMA (YAPAY ZEKA DESTEGI)
   - Bilgisayarda Ollama zaten kuruluysa kurulum sihirbazi otomatik olarak atlar.
   - Kurulu degilse ve kutucuk isaretliyse resmi "ollama.com" adresinden indirir,
     dijital imzasini (Authenticode) dogrular ve otomatik kurar.

3. WINDOWS SMARTSCREEN UYARISI CIKARSA
   - Uygulama henuz ticari kod imzalama sertifikasıyla imzalanmadigi icin ilk acilista
     "Windows kisisel bilgisayarınızı korudu" penceresi cikabilir.
   - Bu durumda "Ek bilgi" (More info) -> "Yine de calistir" (Run anyway) secin.
   - Indirdiginiz dosyanin bozulmadigini "SHA256SUMS.txt" ile karsilastirabilirsiniz:
       Get-FileHash aktapokus-electric-setup.exe -Algorithm SHA256

4. KALDIRMA
   - Windows Ayarlar -> Uygulamalar -> Yuklu uygulamalar icinden "aktapokus-electric"
     secip kaldirabilirsiniz.
"@
[System.IO.File]::WriteAllText((Join-Path $distRoot "KURULUM.txt"), $guide, $utf8NoBom)
Copy-Item (Join-Path $PSScriptRoot "setup.bat") -Destination (Join-Path $distRoot "setup.bat") -Force

Write-Host ""
Write-Host "Tamamlandi:"
Write-Host "  Uygulama (.exe) : $editorExe"
Write-Host "  CLI (.exe)      : $cliExe"
Write-Host "  Kurulum (.exe)  : $setupExe"
Write-Host "  Kurulum (.bat)  : $(Join-Path $distRoot 'setup.bat')"
Write-Host "  SHA-256 Ozeti   : $(Join-Path $distRoot 'SHA256SUMS.txt')"
Write-Host "  Kurulum Rehberi : $(Join-Path $distRoot 'KURULUM.txt')"

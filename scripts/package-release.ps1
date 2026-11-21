#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$VersionTag = "",
    [string]$OutputDir = "build/release-assets",
    [string]$GamePath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$slnPath = Join-Path $repoRoot "CasualtiesTogether.sln"
$libsDir = Join-Path $repoRoot "libs" "dlls"
$refsDir = Join-Path $repoRoot "references"

if (-not (Test-Path -LiteralPath $propsPath)) {
    throw "Directory.Build.props not found at '$propsPath'."
}
if (-not (Test-Path -LiteralPath $slnPath)) {
    throw "Solution file not found at '$slnPath'."
}

[xml]$propsXml = Get-Content -LiteralPath $propsPath -Raw -Encoding utf8
$modVersionNode = $propsXml.SelectSingleNode("//ModVersion")
if ($null -eq $modVersionNode -or [string]::IsNullOrWhiteSpace($modVersionNode.InnerText)) {
    throw "Failed to read <ModVersion> from '$propsPath'."
}
$modVersion = $modVersionNode.InnerText.Trim()

if ([string]::IsNullOrWhiteSpace($VersionTag)) {
    $VersionTag = "v$modVersion"
}

if (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $repoRoot $OutputDir
}

$runtimeDllNames = @(
    "K4os.Compression.LZ4.dll",
    "LiteEntitySystem.dll",
    "LiteNetLib.dll",
    "OpusSharp.Core.dll",
    "opus.dll",
    "RefMagic.dll",
    "System.Runtime.CompilerServices.Unsafe.dll"
)

foreach ($dllName in $runtimeDllNames) {
    $fullDll = Join-Path $libsDir $dllName
    if (-not (Test-Path -LiteralPath $fullDll)) {
        throw "Required runtime dependency missing: '$fullDll'."
    }
}

$buildArgs = @()
$isCi = ($env:CI -eq "true") -or ($env:GITHUB_ACTIONS -eq "true")
if (-not [string]::IsNullOrWhiteSpace($GamePath)) {
    $buildArgs += "-p:BaseGamePath=$GamePath"
} elseif ($isCi) {
    # CI 环境未显式传 GamePath 时传入占位路径屏蔽环境变量干扰，确保强制回退至仓库内的 references/ 桩
    $buildArgs += "-p:BaseGamePath=$(Join-Path $repoRoot '__ci_reference_stubs_only__')"
}

Write-Host "Restoring solution packages..."
& dotnet restore $slnPath | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

function Invoke-ModBuild {
    param([string]$Configuration)

    Write-Host "Building configuration '$Configuration' (--no-incremental)..."
    & dotnet build $slnPath -c $Configuration --no-restore --no-incremental @buildArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build ($Configuration) failed with exit code $LASTEXITCODE"
    }

    $binDir = Join-Path $repoRoot "build" "artifacts" "CasualtiesTogether" "bin" $Configuration
    $dllPath = Join-Path $binDir "CasualtiesMP.dll"
    if (-not (Test-Path -LiteralPath $dllPath)) {
        throw "Expected build output missing: '$dllPath'"
    }
    return $binDir
}

if (-not ("CasualtiesTogether.Build.ProbingLoadContext" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace CasualtiesTogether.Build {
    public sealed class ProbingLoadContext : AssemblyLoadContext {
        private readonly string[] _probeDirectories;

        public ProbingLoadContext(string[] probeDirectories) : base("ModVerifyContext", isCollectible: true) {
            _probeDirectories = probeDirectories ?? Array.Empty<string>();
        }

        protected override Assembly Load(AssemblyName assemblyName) {
            string simpleName = assemblyName.Name;
            // 系统基础类库必须交还 .NET 宿主默认上下文解析，防止真机 Managed 目录下的 Mono mscorlib/netstandard 引发类型系统冲突
            if (string.IsNullOrEmpty(simpleName) ||
                simpleName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                simpleName.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
                simpleName.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                simpleName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            string fileName = simpleName + ".dll";
            foreach (string dir in _probeDirectories) {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) {
                    byte[] raw = File.ReadAllBytes(candidate);
                    return LoadFromStream(new MemoryStream(raw));
                }
            }
            return null;
        }
    }
}
"@
}

function Get-NuGetSearchDirectories {
    $nugetRoot = if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        $env:NUGET_PACKAGES
    } else {
        Join-Path $HOME ".nuget" "packages"
    }

    $dirs = [System.Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $nugetRoot) {
        foreach ($pkg in @("bepinex.baselib", "bepinex.core", "harmonyx", "monomod.runtime.detour", "monomod.utils")) {
            $pkgDir = Join-Path $nugetRoot $pkg
            if (Test-Path -LiteralPath $pkgDir) {
                foreach ($sub in @(Get-ChildItem -LiteralPath $pkgDir -Recurse -Directory -ErrorAction SilentlyContinue)) {
                    if (@(Get-ChildItem -LiteralPath $sub.FullName -Filter "*.dll" -File -ErrorAction SilentlyContinue).Count -gt 0) {
                        $dirs.Add($sub.FullName)
                    }
                }
            }
        }
    }
    return $dirs.ToArray()
}

$nugetProbeDirs = Get-NuGetSearchDirectories

function Assert-ModAssemblyMetadata {
    param(
        [string]$DllPath,
        [bool]$ExpectedRelease,
        [bool]$ExpectedDebug,
        [string]$ExpectedModVersion
    )

    $probeDirs = [System.Collections.Generic.List[string]]::new()
    $dllDir = [System.IO.Path]::GetDirectoryName($DllPath)
    if (-not [string]::IsNullOrWhiteSpace($dllDir)) {
        $probeDirs.Add($dllDir)
    }
    if (-not [string]::IsNullOrWhiteSpace($GamePath)) {
        $candidateManaged = Join-Path $GamePath "CasualtiesUnknown_Data" "Managed"
        if (Test-Path -LiteralPath $candidateManaged) {
            $probeDirs.Add($candidateManaged)
        }
    }
    if (Test-Path -LiteralPath $refsDir) {
        $probeDirs.Add($refsDir)
    }
    $probeDirs.Add($libsDir)
    foreach ($nd in $nugetProbeDirs) {
        $probeDirs.Add($nd)
    }

    # 用内存字节流配合可回收 AssemblyLoadContext 反射读取常量与静态只读字段，避免 Windows 文件锁残留
    $alc = [CasualtiesTogether.Build.ProbingLoadContext]::new($probeDirs.ToArray())

    try {
        $asmBytes = [System.IO.File]::ReadAllBytes($DllPath)
        $asmStream = [System.IO.MemoryStream]::new($asmBytes)
        $asm = $alc.LoadFromStream($asmStream)

        $buildInfoType = $asm.GetType("Together.BuildInfo", $true)
        $buildInfoVer = [string]$buildInfoType.GetField("MOD_VERSION").GetRawConstantValue()

        $pluginType = $asm.GetType("Together.Plugin", $true)
        $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static
        $isRelease = [bool]$pluginType.GetField("IS_RELEASE_BUILD", $flags).GetValue($null)
        $isDebug = [bool]$pluginType.GetField("IS_DEBUG_BUILD", $flags).GetValue($null)
        $isFullgame = [bool]$pluginType.GetField("IS_FULLGAME_BUILD", $flags).GetValue($null)
        $pluginVer = [string]$pluginType.GetField("MOD_VERSION", $flags).GetValue($null)

        if ($buildInfoVer -ne $ExpectedModVersion -or $pluginVer -ne $ExpectedModVersion) {
            throw "MOD_VERSION mismatch in '$DllPath': BuildInfo='$buildInfoVer', Plugin='$pluginVer', Expected='$ExpectedModVersion'."
        }
        if ($isRelease -ne $ExpectedRelease) {
            throw "IS_RELEASE_BUILD mismatch in '$DllPath': Actual=$isRelease, Expected=$ExpectedRelease."
        }
        if ($isDebug -ne $ExpectedDebug) {
            throw "IS_DEBUG_BUILD mismatch in '$DllPath': Actual=$isDebug, Expected=$ExpectedDebug."
        }
        if (-not $isFullgame) {
            throw "IS_FULLGAME_BUILD mismatch in '$DllPath': Expected True for Fullgame release packaging."
        }

        Write-Host "Verified '$DllPath': MOD_VERSION=$pluginVer, IS_RELEASE_BUILD=$isRelease, IS_DEBUG_BUILD=$isDebug, IS_FULLGAME_BUILD=$isFullgame"
    } finally {
        $alc.Unload()
    }
}

function New-PluginZip {
    param(
        [string]$ZipPath,
        [hashtable]$FileMap
    )

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entryPath in ($FileMap.Keys | Sort-Object)) {
            $srcPath = $FileMap[$entryPath]
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                $srcPath,
                $entryPath,
                [System.IO.Compression.CompressionLevel]::Optimal
            )
        }
    } finally {
        $zip.Dispose()
    }
}

$releaseBinDir = Invoke-ModBuild -Configuration "ReleaseFullgame"
$releaseDllSrc = Join-Path $releaseBinDir "CasualtiesMP.dll"
Assert-ModAssemblyMetadata -DllPath $releaseDllSrc -ExpectedRelease $true -ExpectedDebug $false -ExpectedModVersion $modVersion

$debugBinDir = Invoke-ModBuild -Configuration "DebugFullgame"
$debugDllSrc = Join-Path $debugBinDir "CasualtiesMP.dll"
$debugPdbSrc = Join-Path $debugBinDir "CasualtiesMP.pdb"
if (-not (Test-Path -LiteralPath $debugPdbSrc)) {
    throw "Expected portable PDB missing for DebugFullgame: '$debugPdbSrc'"
}
Assert-ModAssemblyMetadata -DllPath $debugDllSrc -ExpectedRelease $false -ExpectedDebug $true -ExpectedModVersion $modVersion

[void](New-Item -ItemType Directory -Path $OutputDir -Force)
foreach ($stale in @(Get-ChildItem -LiteralPath $OutputDir -File -ErrorAction SilentlyContinue)) {
    if ($stale.Name -like "CasualtiesTogether-*.zip" -or $stale.Name -in @("CasualtiesMP.dll", "CasualtiesMP+debug.dll", "SHA256SUMS.txt")) {
        Remove-Item -LiteralPath $stale.FullName -Force
    }
}

$releaseZipName = "CasualtiesTogether-$VersionTag.zip"
$debugZipName = "CasualtiesTogether-$VersionTag+debug.zip"
$releaseDllName = "CasualtiesMP.dll"
$debugDllName = "CasualtiesMP+debug.dll"
$sumsName = "SHA256SUMS.txt"

$releaseZipEntries = @{
    "BepInEx/plugins/Together/CasualtiesMP.dll" = $releaseDllSrc
}
$debugZipEntries = @{
    "BepInEx/plugins/Together/CasualtiesMP.dll" = $debugDllSrc
    "BepInEx/plugins/Together/CasualtiesMP.pdb" = $debugPdbSrc
}
foreach ($dllName in $runtimeDllNames) {
    $libFull = Join-Path $libsDir $dllName
    $releaseZipEntries["BepInEx/plugins/Together/$dllName"] = $libFull
    $debugZipEntries["BepInEx/plugins/Together/$dllName"] = $libFull
}

$releaseZipPath = Join-Path $OutputDir $releaseZipName
$debugZipPath = Join-Path $OutputDir $debugZipName
$releaseDllOut = Join-Path $OutputDir $releaseDllName
$debugDllOut = Join-Path $OutputDir $debugDllName
$sumsPath = Join-Path $OutputDir $sumsName

New-PluginZip -ZipPath $releaseZipPath -FileMap $releaseZipEntries
New-PluginZip -ZipPath $debugZipPath -FileMap $debugZipEntries
Copy-Item -LiteralPath $releaseDllSrc -Destination $releaseDllOut -Force
Copy-Item -LiteralPath $debugDllSrc -Destination $debugDllOut -Force

$assetNames = @($releaseZipName, $debugZipName, $releaseDllName, $debugDllName)
$sumLines = [System.Collections.Generic.List[string]]::new()
foreach ($asset in $assetNames) {
    $assetPath = Join-Path $OutputDir $asset
    $hash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $sumLines.Add("$hash  $asset")
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($sumsPath, ($sumLines -join "`n") + "`n", $utf8NoBom)

Write-Host "Successfully packaged 5 release assets in '$OutputDir':"
foreach ($line in $sumLines) {
    Write-Host "  $line"
}
Write-Host "  $sumsName"

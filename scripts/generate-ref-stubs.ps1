#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$GamePath = "",
    [string]$ManagedDir = "",
    [string]$OutputDir = "",
    [string]$PublicizerLibDir = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$csprojPath = Join-Path $repoRoot "ScavPrototypeMP" "CasualtiesTogether.csproj"
if (-not (Test-Path -LiteralPath $csprojPath)) {
    throw "Project file not found: $csprojPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "references"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $repoRoot $OutputDir
}

if ([string]::IsNullOrWhiteSpace($ManagedDir)) {
    $candidateGamePaths = @()
    if (-not [string]::IsNullOrWhiteSpace($GamePath)) {
        $candidateGamePaths += $GamePath
    }
    if (-not [string]::IsNullOrWhiteSpace($env:CASUALTIESTOGETHER_GAME_PATH)) {
        $candidateGamePaths += $env:CASUALTIESTOGETHER_GAME_PATH
    }
    $candidateGamePaths += @(
        (Join-Path $repoRoot "CU_Fullgame"),
        "D:\SteamLibrary\steamapps\common\Casualties Unknown",
        "C:\Program Files (x86)\Steam\steamapps\common\Casualties Unknown"
    )

    foreach ($candidate in $candidateGamePaths) {
        $candidateManaged = Join-Path $candidate "CasualtiesUnknown_Data" "Managed"
        if (Test-Path -LiteralPath (Join-Path $candidateManaged "Assembly-CSharp.dll")) {
            $ManagedDir = $candidateManaged
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($ManagedDir) -or -not (Test-Path -LiteralPath (Join-Path $ManagedDir "Assembly-CSharp.dll"))) {
    throw "Valid Casualties Unknown Managed directory not found. Pass -GamePath or -ManagedDir pointing to CasualtiesUnknown_Data\Managed."
}

function Resolve-PublicizerDirectory {
    param([string]$ExplicitDir)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitDir)) {
        if (Test-Path -LiteralPath (Join-Path $ExplicitDir "BepInEx.AssemblyPublicizer.dll")) {
            return $ExplicitDir
        }
        throw "BepInEx.AssemblyPublicizer.dll not found in -PublicizerLibDir '$ExplicitDir'."
    }

    $nugetRoot = if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        $env:NUGET_PACKAGES
    } else {
        Join-Path $HOME ".nuget" "packages"
    }

    $pkgRoot = Join-Path $nugetRoot "bepinex.assemblypublicizer.msbuild"
    if (-not (Test-Path -LiteralPath $pkgRoot)) {
        Write-Host "Restoring NuGet packages to populate BepInEx.AssemblyPublicizer.MSBuild..."
        & dotnet restore $csprojPath | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE"
        }
    }

    $versions = @(Get-ChildItem -LiteralPath $pkgRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending)
    foreach ($verDir in $versions) {
        $ns21 = Join-Path $verDir.FullName "lib" "netstandard2.1"
        if (Test-Path -LiteralPath (Join-Path $ns21 "BepInEx.AssemblyPublicizer.dll")) {
            return $ns21
        }
    }

    throw "Could not locate BepInEx.AssemblyPublicizer.dll under '$pkgRoot'."
}

$resolvedPubDir = Resolve-PublicizerDirectory -ExplicitDir $PublicizerLibDir
foreach ($dllName in @("AsmResolver.dll", "AsmResolver.PE.File.dll", "AsmResolver.PE.dll", "AsmResolver.DotNet.dll", "BepInEx.AssemblyPublicizer.dll")) {
    $dllFull = Join-Path $resolvedPubDir $dllName
    if (-not (Test-Path -LiteralPath $dllFull)) {
        throw "Required dependency missing: $dllFull"
    }
    [void][System.Reflection.Assembly]::LoadFrom($dllFull)
}

# 直接从 csproj 解析所有 $(GameManagedDir) 引用，避免脚本与项目文件维护两份名单而失步
[xml]$projXml = Get-Content -LiteralPath $csprojPath -Raw -Encoding utf8
$managedDllNames = [System.Collections.Generic.List[string]]::new()
$prefix = '$(GameManagedDir)\'
foreach ($refNode in $projXml.SelectNodes("//Reference[HintPath]")) {
    $hint = [string]$refNode.HintPath
    $normalizedHint = $hint.Replace('/', '\').Trim()
    if ($normalizedHint.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        $fileName = $normalizedHint.Substring($prefix.Length)
        if (-not $managedDllNames.Contains($fileName)) {
            $managedDllNames.Add($fileName)
        }
    }
}

if ($managedDllNames.Count -eq 0) {
    throw "No `$(GameManagedDir) references found in $csprojPath"
}

[void](New-Item -ItemType Directory -Path $OutputDir -Force)

# Target = None 保留原始 public/private/internal 访问修饰符，构建时再由 MSBuild Publicize="true" 按需公开，保证与真机编译语义一致
$stripOptions = [BepInEx.AssemblyPublicizer.AssemblyPublicizerOptions]::new()
$stripOptions.Strip = $true
$stripOptions.Target = [BepInEx.AssemblyPublicizer.PublicizeTarget]::None
$stripOptions.IncludeOriginalAttributesAttribute = $false

$expectedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$totalSrcBytes = 0L
$totalDstBytes = 0L

Write-Host "Generating $($managedDllNames.Count) metadata reference stubs from '$ManagedDir' -> '$OutputDir'..."

foreach ($dllFileName in $managedDllNames) {
    $srcFile = Join-Path $ManagedDir $dllFileName
    if (-not (Test-Path -LiteralPath $srcFile)) {
        throw "Referenced game assembly missing in ManagedDir: $srcFile"
    }

    $dstFile = Join-Path $OutputDir $dllFileName
    [void]$expectedSet.Add($dllFileName)
    $totalSrcBytes += (Get-Item -LiteralPath $srcFile).Length

    $asmDef = [AsmResolver.DotNet.AssemblyDefinition]::FromFile($srcFile)
    [void][BepInEx.AssemblyPublicizer.AssemblyPublicizer]::Publicize($asmDef, $stripOptions)
    foreach ($module in $asmDef.Modules) {
        $module.Resources.Clear()
    }
    $asmDef.Write($dstFile)
    $totalDstBytes += (Get-Item -LiteralPath $dstFile).Length
}

foreach ($existing in @(Get-ChildItem -LiteralPath $OutputDir -Filter "*.dll" -File -ErrorAction SilentlyContinue)) {
    if (-not $expectedSet.Contains($existing.Name)) {
        Write-Host "Removing stale stub: $($existing.Name)"
        Remove-Item -LiteralPath $existing.FullName -Force
    }
}

$srcMb = [Math]::Round($totalSrcBytes / 1MB, 2)
$dstMb = [Math]::Round($totalDstBytes / 1MB, 2)
Write-Host "Done: $($managedDllNames.Count) stubs written ($srcMb MB -> $dstMb MB)."

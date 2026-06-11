<#
.SYNOPSIS
    Script de publication pour Scrubx.Cli.
.DESCRIPTION
    Ce script facilite la compilation et la publication du binaire standalone Scrubx.Cli.
    Par défaut, il détecte le système d'exploitation actuel et l'architecture pour compiler la cible locale,
    mais il permet également de spécifier un runtime particulier ou de tout compiler d'un coup.
.PARAMETER Runtime
    Le Runtime Identifier (RID) cible (ex: osx-arm64, win-x64, linux-x64).
.PARAMETER Configuration
    La configuration de build (Release par défaut, Debug).
.PARAMETER All
    Si spécifié, compile pour les trois plateformes majeures (osx-arm64, win-x64, linux-x64).
.EXAMPLE
    ./publish.ps1
    Publie pour le système actuel en mode Release.
.EXAMPLE
    ./publish.ps1 -Runtime win-x64
    Publie spécifiquement pour Windows 64-bit.
.EXAMPLE
    ./publish.ps1 -All
    Publie pour macOS, Windows et Linux.
#>

param (
    [string]$Runtime = $null,
    [string]$Configuration = "Release",
    [switch]$All
)

# Détection de l'architecture par défaut
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLower()
if ($IsOSX) {
    $DefaultRuntime = "osx-$architecture"
} elseif ($IsWindows) {
    $DefaultRuntime = "win-$architecture"
} elseif ($IsLinux) {
    $DefaultRuntime = "linux-$architecture"
} else {
    # Fallback par défaut si non détecté
    $DefaultRuntime = "osx-arm64"
}

if (-not $Runtime -and -not $All) {
    $Runtime = $DefaultRuntime
}

# Runtimes cibles à compiler
$RuntimesToPublish = @()
if ($All) {
    $RuntimesToPublish = @("osx-arm64", "win-x64", "linux-x64")
} else {
    $RuntimesToPublish = @($Runtime)
}

# Emplacement du projet
$ProjectPath = Join-Path $PSScriptRoot "src/Scrubx.Cli/Scrubx.Cli.csproj"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "    PUBLICATION STANDALONE DE SCRUBX.CLI     " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "Configuration : $Configuration" -ForegroundColor Gray

foreach ($target in $RuntimesToPublish) {
    Write-Host "`nPublication en cours pour la cible : $target..." -ForegroundColor Yellow

    # Appel de dotnet publish
    dotnet publish $ProjectPath -c $Configuration -r $target

    if ($LASTEXITCODE -eq 0) {
        $PublishDir = Join-Path $PSScriptRoot "src/Scrubx.Cli/bin/$Configuration/net10.0/$target/publish"
        Write-Host "Publication réussie ! Binaire disponible dans :" -ForegroundColor Green
        Write-Host "  $PublishDir" -ForegroundColor White

        # Déterminer le nom du binaire produit
        $binaryName = if ($target -like "win-*") { "Scrubx.Cli.exe" } else { "Scrubx.Cli" }
        $srcBinary = Join-Path $PublishDir $binaryName

        # Nom du fichier avec suffixe
        $destSuffixed = if ($target -like "win-*") { "Scrubx.Cli-$target.exe" } else { "Scrubx.Cli-$target" }
        $destSuffixedPath = Join-Path $PSScriptRoot $destSuffixed

        Copy-Item $srcBinary $destSuffixedPath -Force
        Write-Host "  -> Copié à la racine : ./$destSuffixed" -ForegroundColor Gray

        # Si cible unique ou cible par défaut, copier sous le nom simple
        if (-not $All -or $target -eq $DefaultRuntime) {
            $destDefaultPath = Join-Path $PSScriptRoot $binaryName
            Copy-Item $srcBinary $destDefaultPath -Force
            Write-Host "  -> Copié à la racine (binaire par défaut) : ./$binaryName" -ForegroundColor Gray
        }
    } else {
        Write-Error "La publication a échoué pour la cible $target avec le code de sortie $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

Write-Host "`n=============================================" -ForegroundColor Cyan
Write-Host "    PROCESSUS DE PUBLICATION TERMINÉ !       " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

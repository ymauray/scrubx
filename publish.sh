#!/bin/bash
# Script de publication pour Scrubx.Cli
# Usage: ./publish.sh [-r runtime] [-c configuration] [-a]

set -e

# Valeurs par défaut
CONFIGURATION="Release"
RUNTIME=""
ALL=false

# Détecter l'architecture locale par défaut
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    DEFAULT_RUNTIME="osx-arm64"
else
    DEFAULT_RUNTIME="osx-x64"
fi

usage() {
    echo "Usage: $0 [-r runtime] [-c configuration] [-a]"
    echo "  -r : Spécifier le Runtime Identifier (ex: osx-arm64, win-x64, linux-x64)"
    echo "  -c : Spécifier la configuration de build (Release par défaut)"
    echo "  -a : Compiler pour toutes les plateformes majeures (osx-arm64, win-x64, linux-x64)"
    exit 1
}

while getopts "r:c:ah" opt; do
    case "$opt" in
        r) RUNTIME=$OPTARG ;;
        c) CONFIGURATION=$OPTARG ;;
        a) ALL=true ;;
        h) usage ;;
        *) usage ;;
    esac
done

if [ -z "$RUNTIME" ]; then
    RUNTIME=$DEFAULT_RUNTIME
fi

# Runtimes cibles à compiler
if [ "$ALL" = true ]; then
    RUNTIMES=("osx-arm64" "win-x64" "linux-x64")
else
    RUNTIMES=("$RUNTIME")
fi

PROJECT_PATH="src/Scrubx.Cli/Scrubx.Cli.csproj"

echo "============================================="
echo "    PUBLICATION STANDALONE DE SCRUBX.CLI     "
echo "============================================="
echo "Configuration : $CONFIGURATION"

for target in "${RUNTIMES[@]}"; do
    echo -e "\nPublication en cours pour la cible : $target..."
    
    # Exécution de dotnet publish
    dotnet publish "$PROJECT_PATH" -c "$CONFIGURATION" -r "$target"
    
    PUBLISH_DIR="src/Scrubx.Cli/bin/$CONFIGURATION/net10.0/$target/publish"
    echo -e "\nPublication réussie ! Binaire disponible dans :"
    echo "  $PUBLISH_DIR"

    # Déterminer le nom du fichier binaire produit
    if [[ "$target" == win-* ]]; then
        BINARY_NAME="Scrubx.Cli.exe"
    else
        BINARY_NAME="Scrubx.Cli"
    fi

    SRC_BINARY="$PUBLISH_DIR/$BINARY_NAME"

    # Nommer le binaire dans la racine avec le runtime (ex: Scrubx.Cli-osx-arm64)
    if [[ "$target" == win-* ]]; then
        DEST_SUFFIXED="Scrubx.Cli-$target.exe"
    else
        DEST_SUFFIXED="Scrubx.Cli-$target"
    fi

    cp "$SRC_BINARY" "./$DEST_SUFFIXED"
    echo "  -> Copié à la racine : ./$DEST_SUFFIXED"

    # Si c'est la seule cible, ou s'il s'agit du runtime de l'hôte actuel, on le copie sous le nom de base
    if [ "$ALL" = false ] || [ "$target" = "$DEFAULT_RUNTIME" ]; then
        cp "$SRC_BINARY" "./$BINARY_NAME"
        echo "  -> Copié à la racine (binaire par défaut) : ./$BINARY_NAME"
    fi
done

echo "============================================="
echo "    PROCESSUS DE PUBLICATION TERMINÉ !       "
echo "============================================="

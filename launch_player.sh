#!/bin/bash
set -euo pipefail

PORT=${1:-7777}

echo "=========================================="
echo "  🎮 GAUNIV - CLIENT TERMINAL MORPION (TCP) "
echo "=========================================="

PROJECT_DIR="/workspaces/TP2_NET_PJB/Gauniv.TerminalClient"
cd "$PROJECT_DIR"
echo ""

echo "📦 Compilation"
    if dotnet build -v minimal; then
    echo "✅ Build OK"
else
    echo "⚠️  Build échoué. Nettoyage des caches et nouvelle tentative..."
    # Corrige les erreurs MSB3492 (fichiers cache corrompus/verrouillés)
    rm -f obj/Debug/net10.0/*.cache 2>/dev/null || true
    rm -f obj/Debug/net10.0/*.editorconfig 2>/dev/null || true
    rm -f obj/Debug/net10.0/*.dll 2>/dev/null || true
    dotnet clean -v minimal || true
    dotnet restore -v minimal
    echo "📦 Compilation (tentative 2)..."
    dotnet build -v minimal
    echo "✅ Build OK (après nettoyage)"
fi

# recupere le nom du joueur
read -p "Entrez le nom du joueur: " PLAYER_NAME

echo ""
echo "🚀 Démarrage du client sur le port ${PORT}..."
exec dotnet run -- --host 127.0.0.1 --port "${PORT}" --name "${PLAYER_NAME}"
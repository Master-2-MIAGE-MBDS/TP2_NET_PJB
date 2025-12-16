#!/bin/bash
set -euo pipefail

PORT=${1:-7777}
PLAYER_NAME=${2:-}

echo ""
echo "╔════════════════════════════════════════╗"
echo "║  🎮 GAUNIV - CLIENT TERMINAL MORPION   ║"
echo "╚════════════════════════════════════════╝"
echo ""

PROJECT_DIR="/workspaces/TP2_NET_PJB/Gauniv.TerminalClient"
cd "$PROJECT_DIR"

# Build
echo "📦 Compilation du client..."
if dotnet build -v minimal 2>/dev/null; then
    echo "✅ Build réussi"
else
    echo "⚠️  Build échoué. Nettoyage..."
    rm -f obj/Debug/net10.0/*.cache 2>/dev/null || true
    rm -f obj/Debug/net10.0/*.editorconfig 2>/dev/null || true
    dotnet clean -v minimal 2>/dev/null || true
    dotnet restore -v minimal 2>/dev/null || true
    echo "📦 Nouvelle tentative de build..."
    dotnet build -v minimal
    echo "✅ Build réussi (après nettoyage)"
fi

# Get player name if not provided
if [ -z "$PLAYER_NAME" ]; then
    read -p "Entrez le nom du joueur: " PLAYER_NAME
    if [ -z "$PLAYER_NAME" ]; then
        PLAYER_NAME="Player-$(shuf -i 1000-9999 -n 1)"
    fi
fi

echo ""
echo "🚀 Démarrage du client sur le port ${PORT}..."
echo "👤 Joueur: $PLAYER_NAME"
echo ""

exec dotnet run -- --host 127.0.0.1 --port "${PORT}" --name "${PLAYER_NAME}"
#!/bin/bash
set -euo pipefail

PORT=${1:-7777}

echo ""
echo "╔════════════════════════════════════════╗"
echo "║  🎮 GAUNIV - SERVEUR MORPION (TCP)    ║"
echo "║     Max 2 joueurs par partie            ║"
echo "╚════════════════════════════════════════╝"
echo ""

PROJECT_DIR="/workspaces/TP2_NET_PJB/Gauniv.GameServer"
cd "$PROJECT_DIR"

echo "📦 Compilation du serveur..."
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

echo ""
echo "🚀 Démarrage du serveur sur le port ${PORT}..."
echo "   Astuce: Ctrl+C pour arrêter proprement"
echo ""

exec dotnet run -- "${PORT}"

#!/bin/bash
set -euo pipefail

PORT=${1:-7777}

echo "=========================================="
echo "  🎮 GAUNIV - SERVEUR MORPION (TCP)"
echo "=========================================="
echo ""

PROJECT_DIR="/workspaces/TP2_NET_PJB/Gauniv.GameServer"
cd "$PROJECT_DIR"

echo "📦 Compilation (tentative 1)..."
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

echo ""
echo "🚀 Démarrage du serveur sur le port ${PORT}..."
echo "   Astuce: Ctrl+C pour arrêter proprement"
echo ""

# Lance le serveur en avant-plan pour voir les logs et permettre Ctrl+C
exec dotnet run -- "${PORT}"

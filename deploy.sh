#!/usr/bin/env bash
# =============================================================================
# StoicTrade Backend Deploy Script
# Usage: bash deploy.sh [server_user@host] [app_dir]
# Defaults: root@api.stoictrade.in, /root/StoicTrade
# =============================================================================

set -euo pipefail

REMOTE_HOST="${1:-root@api.stoictrade.in}"
REMOTE_DIR="${2:-/root/StoicTrade}"
BRANCH="development"

echo ""
echo "═══════════════════════════════════════════════════"
echo "  StoicTrade — Backend Deploy"
echo "  Host : $REMOTE_HOST"
echo "  Dir  : $REMOTE_DIR"
echo "  Branch: $BRANCH"
echo "═══════════════════════════════════════════════════"
echo ""

ssh -o StrictHostKeyChecking=no "$REMOTE_HOST" bash <<ENDSSH
set -euo pipefail

echo ">>> [1/5] Navigating to app directory..."
cd "$REMOTE_DIR"

echo ">>> [2/5] Pulling latest changes from $BRANCH..."
git fetch origin
git checkout $BRANCH
git pull origin $BRANCH

echo ">>> [3/5] Building & restarting Docker containers..."
docker compose -f docker-compose.prod.yml up -d --build --remove-orphans

echo ">>> [4/5] Removing dangling Docker images..."
docker image prune -f

echo ">>> [5/5] Checking container health..."
sleep 5
docker compose -f docker-compose.prod.yml ps

echo ""
echo "✅ Deploy complete!"
ENDSSH

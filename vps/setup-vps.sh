#!/usr/bin/env bash
set -e

echo "============================================================"
echo "   COLISEUDESK SERVER - INSTALADOR AUTOMÁTICO PARA VPS"
echo "   Coliseu Sistemas"
echo "============================================================"

# 1. Verifica/instala Docker se não existir
if ! command -v docker &> /dev/null; then
    echo "[1/4] Docker não encontrado. Instalando Docker..."
    curl -fsSL https://get.docker.com | sh
else
    echo "[1/4] Docker já está instalado."
fi

# 2. Detecta o IP Público da VPS
PUBLIC_IP=$(curl -4 -s https://ifconfig.me || curl -4 -s https://api.ipify.org || hostname -I | awk '{print $1}')
echo "[2/4] IP Público detectado da VPS: ${PUBLIC_IP}"

# Cria arquivo .env com o IP da VPS
echo "RELAY_HOST=${PUBLIC_IP}" > .env

# 3. Libera portas no UFW caso esteja ativo
if command -v ufw &> /dev/null && ufw status | grep -q "Status: active"; then
    echo "[3/4] Liberando portas do ColiseuDesk no firewall UFW..."
    ufw allow 21115:21119/tcp
    ufw allow 21116/udp
else
    echo "[3/4] Firewall UFW inativo ou não utilizado (verifique o painel da VPS se necessário)."
fi

# 4. Sobe os containers do servidor ColiseuDesk
echo "[4/4] Iniciando containers ColiseuDesk (hbbs + hbbr)..."
mkdir -p ./data
docker compose up -d

# Aguarda a geração da chave pública criptográfica
echo "Aguardando geração da chave criptográfica (id_ed25519.pub)..."
for i in {1..15}; do
    if [ -f "./data/id_ed25519.pub" ]; then
        break
    fi
    sleep 1
done

PUB_KEY=$(cat ./data/id_ed25519.pub 2>/dev/null || echo "Chave ainda sendo gerada em ./data/id_ed25519.pub")

echo ""
echo "============================================================"
echo " ✅ SERVIDOR COLISEUDESK ATIVO NA VPS!"
echo "============================================================"
echo "Copie os 2 dados abaixo para compilar o seu ColiseuDesk.exe"
echo "no GitHub Actions (ou em config/coliseudesk.json):"
echo ""
echo "  👉 RENDEZVOUS_SERVER (IP da VPS) : ${PUBLIC_IP}"
echo "  👉 RS_PUB_KEY (Chave Pública)    : ${PUB_KEY}"
echo "============================================================"

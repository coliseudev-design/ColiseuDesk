<p align="center">
  <img src="assets/logo-coliseu.png" alt="Coliseu Sistemas" width="360"/>
</p>

<h1 align="center">ColiseuDesk — Acesso Remoto Corporativo</h1>

<p align="center">
  <strong>Plataforma de Suporte Remoto da Coliseu Sistemas</strong><br/>
  Interface clássica de alta produtividade estilo AnyDesk + Motor de Vídeo/Áudio/Arquivos <strong>RustDesk</strong> + Servidor Próprio em VPS.
</p>

---

## 🚀 Recursos Principais

- **Mesmo Programa para Cliente e Técnico**:
  - **Para o Cliente (Receber Suporte)**: Abre sem precisar configurar nada e já exibe o **ID de 9 dígitos** e a **Senha Temporária** conectados direto na VPS da Coliseu Sistemas.
  - **Para o Técnico (Dar Suporte)**: Barra superior de controle remoto inspirada no AnyDesk com abas de múltiplas conexões, troca rápida de monitor (`[1]` / `[2]`), transferência de arquivos, chat, atalhos (`Ctrl+Alt+Del`), transmissão de som HD, permissões de segurança (bloquear mouse/cortina) e quadro branco (desenhar na tela do cliente).
  - **Acesso Não Supervisionado (Senha Fixa)**: Configure uma senha permanente nos servidores e caixas (PDVs) dos clientes para acessar a qualquer momento.
- **Identidade Visual Coliseu Sistemas**:
  - Azul Primário (`#0072BF`), Azul Ciano (`#0094D2`) e Cinza Grafite (`#4C4C4C`), com logotipo e ícone `.ico` oficiais embutidos.

---

## 🖥️ Passo 1: Subir o Servidor na sua VPS (Docker Compose)

Conecte via SSH na sua VPS Linux (Ubuntu / Debian) e execute os comandos abaixo:

### Opção A — Instalação Automática com 1 Comando
```bash
git clone https://github.com/coliseudev-design/ColiseuDesk.git
cd ColiseuDesk
chmod +x vps/setup-vps.sh
sudo ./vps/setup-vps.sh
```
O script instala o Docker (se não tiver), detecta o IP da sua VPS, abre as portas necessárias, sobe os serviços `hbbs` e `hbbr` e **exibe na tela o seu IP e a sua Chave Pública (`RS_PUB_KEY`)**.

### Opção B — Usando `docker compose` Manualmente
Na raiz deste projeto já estão prontos dois arquivos Docker Compose:
- [`docker-compose.yml`](docker-compose.yml): Recomendado para VPS Linux dedicada (`network_mode: "host"`).
- [`docker-compose.ports.yml`](docker-compose.ports.yml): Recomendado para painéis como **Portainer, Easypanel ou Coolify** (mapeamento explícito das portas `21115` a `21119`).

```bash
# Definir o IP ou domínio da sua VPS:
export RELAY_HOST=SEU_IP_DA_VPS

# Subir os containers em segundo plano:
docker compose up -d

# Ver a Chave Pública gerada (copie esse valor!):
cat ./data/id_ed25519.pub
```

> **Portas utilizadas na VPS**:
> - **TCP**: `21115`, `21116`, `21117`, `21118`, `21119`
> - **UDP**: `21116`

---

## ☁️ Passo 2: Gerar o `ColiseuDesk.exe` na Nuvem (GitHub Actions)

Você não precisa instalar compiladores pesados no seu computador. O próprio GitHub gera o executável pronto para você:

1. No repositório do GitHub, clique na aba **Actions**.
2. Selecione o workflow **"Gerar ColiseuDesk.exe (Build Rápido Windows)"**.
3. Clique no botão **"Run workflow"** e preencha:
   - **IP ou Domínio da sua VPS**: O IP da sua VPS onde subiu o Docker Compose.
   - **Chave Pública gerada na VPS**: O texto exibido pelo arquivo `./data/id_ed25519.pub`.
4. Em cerca de **1 a 2 minutos**, baixe o pacote pronto **`ColiseuDesk-Windows-x64`** na seção *Artifacts* da execução!

*(Dica: Você também pode salvar `RENDEZVOUS_SERVER` e `RS_PUB_KEY` em **Settings → Secrets and variables → Actions** do repositório para que todo push já saia configurado automaticamente com a sua VPS).*

---

## 📂 Estrutura do Projeto

```text
ColiseuDesk/
├── .github/workflows/
│   ├── build-coliseudesk.yml       # Gera o ColiseuDesk.exe pronto na nuvem em ~2 min
│   └── build-rustdesk-engine.yml   # Aplica branding direto no código-fonte C++/Rust/Flutter
├── assets/
│   ├── logo-coliseu.png            # Logotipo oficial Coliseu Sistemas (fundo transparente)
│   ├── icon.png                    # Emblema Coliseu 256x256
│   ├── icon.ico                    # Ícone nativo do executável Windows (.exe)
│   └── icon.svg                    # Ícone vetorial SVG
├── config/
│   └── coliseudesk.json            # Configurações de cores, marca e servidor VPS
├── scripts/
│   └── apply-coliseu-branding.py   # Patcher automático de código-fonte do motor RustDesk
├── src/ColiseuDesk/                # Aplicação Desktop Windows (WPF .NET 8 + Barra AnyDesk)
├── vps/
│   └── setup-vps.sh                # Instalador automático do servidor para VPS Linux
├── docker-compose.yml              # Docker Compose principal (modo host)
└── docker-compose.ports.yml        # Docker Compose com portas explícitas (Portainer/Easypanel)
```

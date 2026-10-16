#!/usr/bin/env python3
"""
Aplica a identidade visual da Coliseu Sistemas (ColiseuDesk),
configurações da VPS (hbbs/hbbr) e o tema inspirado no AnyDesk
diretamente sobre o código-fonte do motor RustDesk antes da compilação.
"""

import json
import os
import re
import shutil
import sys
from pathlib import Path


def replace_in_file(file_path: Path, pattern: str, replacement: str, flags: int = 0) -> bool:
    if not file_path.exists():
        print(f"  [aviso] Arquivo não encontrado (ignorado): {file_path}")
        return False
    content = file_path.read_text(encoding="utf-8", errors="ignore")
    new_content, count = re.subn(pattern, replacement, content, flags=flags)
    if count > 0:
        file_path.write_text(new_content, encoding="utf-8")
        print(f"  [ok] {file_path} atualizado ({count} substituição(ões))")
        return True
    return False


def main():
    if len(sys.argv) < 2:
        print("Uso: python apply-coliseu-branding.py <caminho_codigo_rustdesk>")
        sys.exit(1)

    repo_root = Path(__file__).resolve().parent.parent
    rustdesk_dir = Path(sys.argv[1]).resolve()
    config_path = repo_root / "config" / "coliseudesk.json"

    with open(config_path, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    app_name = cfg.get("app_name", "ColiseuDesk")
    company_name = cfg.get("company_name", "Coliseu Sistemas")
    primary_hex = cfg["branding"]["primary_color"].lstrip("#")
    secondary_hex = cfg["branding"]["secondary_color"].lstrip("#")

    rendezvous_server = os.environ.get("RENDEZVOUS_SERVER", "").strip() or cfg["server"].get("rendezvous_server", "").strip()
    rs_pub_key = os.environ.get("RS_PUB_KEY", "").strip() or cfg["server"].get("rs_pub_key", "").strip()
    api_server = os.environ.get("API_SERVER", "").strip() or cfg["server"].get("api_server", "").strip()

    print("============================================================")
    print(f" Aplicando Customização: {app_name} ({company_name})")
    print(f" Diretório do Motor    : {rustdesk_dir}")
    print(f" Servidor VPS (ID/IP)  : {rendezvous_server or '[Padrão/Configurável]'}")
    print(f" Chave Pública (Key)   : {(rs_pub_key[:12] + '...') if rs_pub_key else '[Não definida]'}")
    print("============================================================")

    # 1. Embutir Servidor VPS e Chave Pública em libs/hbb_common/src/config.rs
    config_rs = rustdesk_dir / "libs" / "hbb_common" / "src" / "config.rs"
    if config_rs.exists():
        if rendezvous_server:
            replace_in_file(
                config_rs,
                r'pub const RENDEZVOUS_SERVERS:\s*&\[&str\]\s*=\s*&\[[^\]]*\];',
                f'pub const RENDEZVOUS_SERVERS: &[&str] = &["{rendezvous_server}"];',
                flags=re.DOTALL,
            )
        if rs_pub_key:
            replace_in_file(
                config_rs,
                r'pub const RS_PUB_KEY:\s*&str\s*=\s*"[^"]*";',
                f'pub const RS_PUB_KEY: &str = "{rs_pub_key}";',
            )
        replace_in_file(
            config_rs,
            r'pub static ref APP_NAME:\s*Arc<RwLock<String>>\s*=\s*Arc::new\(RwLock::new\("RustDesk"\.to_owned\(\)\)\);',
            f'pub static ref APP_NAME: Arc<RwLock<String>> = Arc::new(RwLock::new("{app_name}".to_owned()));',
        )

    # 2. Embutir API Server opcional em src/common.rs
    common_rs = rustdesk_dir / "src" / "common.rs"
    if common_rs.exists() and api_server:
        replace_in_file(
            common_rs,
            r'pub const DEFAULT_API_SERVER:\s*&str\s*=\s*"[^"]*";',
            f'pub const DEFAULT_API_SERVER: &str = "{api_server}";',
        )

    # 3. Atualizar cores e tema da Coliseu Sistemas no Flutter (flutter/lib/common.dart)
    common_dart = rustdesk_dir / "flutter" / "lib" / "common.dart"
    if common_dart.exists():
        replace_in_file(
            common_dart,
            r"Color\(0xFF0071FF\)",
            f"Color(0xFF{primary_hex})",
        )
        replace_in_file(
            common_dart,
            r"Color\(0xFF2B5B84\)",
            f"Color(0xFF{secondary_hex})",
        )

    # 4. Atualizar nome da aplicação no Windows Runner (C++ / RC)
    runner_rc = rustdesk_dir / "flutter" / "windows" / "runner" / "Runner.rc"
    if runner_rc.exists():
        replace_in_file(runner_rc, r'"RustDesk"', f'"{app_name}"')
        replace_in_file(runner_rc, r'"Purslane Ltd"', f'"{company_name}"')

    main_cpp = rustdesk_dir / "flutter" / "windows" / "runner" / "main.cpp"
    if main_cpp.exists():
        replace_in_file(main_cpp, r'L"RustDesk"', f'L"{app_name}"')

    # 5. Substituir ícones e logotipos pelos da Coliseu Sistemas
    icon_ico = repo_root / cfg["branding"]["icon_ico_path"]
    icon_png = repo_root / cfg["branding"]["icon_png_path"]
    icon_svg = repo_root / cfg["branding"]["icon_svg_path"]

    ico_targets = [
        rustdesk_dir / "res" / "icon.ico",
        rustdesk_dir / "res" / "tray-icon.ico",
        rustdesk_dir / "flutter" / "windows" / "runner" / "resources" / "app_icon.ico",
    ]
    for target in ico_targets:
        if icon_ico.exists() and target.parent.exists():
            shutil.copy2(icon_ico, target)
            print(f"  [ok] Ícone .ico copiado para: {target}")

    png_targets = [
        rustdesk_dir / "res" / "icon.png",
        rustdesk_dir / "res" / "32x32.png",
        rustdesk_dir / "res" / "128x128.png",
        rustdesk_dir / "res" / "128x128@2x.png",
    ]
    for target in png_targets:
        if icon_png.exists() and target.parent.exists():
            shutil.copy2(icon_png, target)
            print(f"  [ok] Ícone .png copiado para: {target}")

    svg_targets = [
        rustdesk_dir / "res" / "icon.svg",
        rustdesk_dir / "flutter" / "assets" / "icon.svg",
    ]
    for target in svg_targets:
        if icon_svg.exists() and target.parent.exists():
            shutil.copy2(icon_svg, target)
            print(f"  [ok] Ícone .svg copiado para: {target}")

    print("============================================================")
    print(f" ✅ Customização do {app_name} concluída com sucesso!")
    print("============================================================")


if __name__ == "__main__":
    main()

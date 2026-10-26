#!/usr/bin/env python3
"""Fotos dos produtos do seed (#147): CC0 ou domínio público, do Openverse.

Dois passos, para a escolha das fotos ser revisada por uma pessoa e o resultado ser reproduzível:

  python3 scripts/media/product_images.py fetch   # baixa candidatas e monta uma prancha por subcategoria
  # revisar .cache/media/<sub>/prancha.jpg e anotar os ids escolhidos em scripts/media/selection.json
  python3 scripts/media/product_images.py build   # gera infra/media/produtos/*.webp e os créditos

Cada foto sai em WebP 800x800 (recorte central): o front (next/image) conhece as dimensões de antemão.
Precisa de Pillow (pip install pillow).
"""
import json
import sys
import time
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / ".cache" / "media"
SELECTION = Path(__file__).with_name("selection.json")
OUT = ROOT / "infra" / "media" / "produtos"
CREDITS = ROOT / "infra" / "media" / "CREDITOS.md"

SIZE = 800
CANDIDATES = 12
API = "https://api.openverse.org/v1/images/"
# fontes de fotografia CC0/domínio público; a Wikimedia traz muito logo e diagrama
SOURCES = "stocksnap,rawpixel"
USER_AGENT = "compra-facil-seed/1.0 (https://github.com/mateus-vitor-ferreira-dev/compra-facil)"

# subcategoria do seed (services/catalog/cmd/seed/catalog_data.go) -> busca em inglês
QUERIES = {
    "tv": "television",
    "fones": "headphones",
    "caixas-de-som": "speaker",
    "notebooks": "laptop",
    "monitores": "computer monitor",
    "perifericos": "keyboard",
    "geladeiras": "refrigerator",
    "fogoes": "stove",
    "lavadoras": "washing machine",
    "sofas": "sofa",
    "colchoes": "bed",
    "bicicletas": "bicycle",
    "fitness": "dumbbell",
}


def get(url, timeout=60):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return response.read()


def search(query):
    params = urllib.parse.urlencode({
        "q": query, "license": "cc0,pdm", "source": SOURCES, "category": "photograph",
        "page_size": CANDIDATES, "mature": "false",
    })
    for attempt in range(3):
        try:
            return json.loads(get(f"{API}?{params}", timeout=120))["results"]
        except Exception as e:  # a API pública é lenta e às vezes cai
            print(f"  {query}: {e}; tentando de novo", file=sys.stderr)
            time.sleep(5 * (attempt + 1))
    return []


def square(path):
    image = ImageOps.exif_transpose(Image.open(path)).convert("RGB")
    return ImageOps.fit(image, (SIZE, SIZE), Image.Resampling.LANCZOS, centering=(0.5, 0.5))


def fetch_one(sub, query):
    folder = CACHE / sub
    folder.mkdir(parents=True, exist_ok=True)
    results = search(query)
    kept = []
    for r in results:
        path = folder / f"{r['id']}.img"
        try:
            if not path.exists():
                path.write_bytes(get(r["url"]))
            square(path)  # descarta o que não abre
            kept.append(r)
        except Exception as e:
            print(f"  {sub} {r['id']}: {e}", file=sys.stderr)
    (folder / "meta.json").write_text(json.dumps(kept, ensure_ascii=False, indent=1))
    sheet(sub, kept)
    print(f"{sub}: {len(kept)} candidatas")


def sheet(sub, results):
    thumb, cols = 260, 4
    rows = (len(results) + cols - 1) // cols
    board = Image.new("RGB", (cols * thumb, max(rows, 1) * (thumb + 22)), "white")
    draw = ImageDraw.Draw(board)
    for i, r in enumerate(results):
        x, y = (i % cols) * thumb, (i // cols) * (thumb + 22)
        board.paste(square(CACHE / sub / f"{r['id']}.img").resize((thumb - 6, thumb - 6)), (x + 3, y + 3))
        draw.text((x + 4, y + thumb), f"{i}: {r['id'][:8]}", fill="black")
    board.save(CACHE / sub / "prancha.jpg", quality=80)


def fetch():
    with ThreadPoolExecutor(max_workers=3) as pool:
        list(pool.map(lambda item: fetch_one(*item), QUERIES.items()))


def build():
    selection = json.loads(SELECTION.read_text())
    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.webp"):
        old.unlink()
    lines = [
        "# Créditos das fotos de produto",
        "",
        "Fotos em domínio público ou CC0, encontradas pelo [Openverse](https://openverse.org) e convertidas para",
        f"WebP {SIZE}x{SIZE} (recorte central) por `scripts/media/product_images.py`. Não é exigida atribuição; os",
        "créditos ficam aqui por transparência. As marcas citadas nos nomes dos produtos do seed são só massa",
        "de teste: as fotos não mostram esses produtos nem têm relação com essas marcas.",
        "",
        "| Arquivo | Título | Autor | Licença | Origem |",
        "|---|---|---|---|---|",
    ]
    for sub, ids in selection.items():
        meta = {r["id"]: r for r in json.loads((CACHE / sub / "meta.json").read_text())}
        for n, short in enumerate(ids, start=1):
            r = next(v for k, v in meta.items() if k.startswith(short))
            name = f"{sub}-{n}.webp"
            square(CACHE / sub / f"{r['id']}.img").save(OUT / name, "WEBP", quality=80, method=6)
            license_ = f"{r['license'].upper()} {r.get('license_version') or ''}".strip()
            title = (r.get("title") or "").replace("|", "/")
            creator = (r.get("creator") or "—").replace("|", "/")
            lines.append(f"| `{name}` | {title} | {creator} | {license_} | [{r['source']}]({r['foreign_landing_url']}) |")
    CREDITS.write_text("\n".join(lines) + "\n")
    total = sum(len(v) for v in selection.values())
    size = sum(p.stat().st_size for p in OUT.glob("*.webp")) / 1024
    print(f"{total} fotos em {OUT.relative_to(ROOT)} ({size:.0f} KiB) e créditos em {CREDITS.relative_to(ROOT)}")


if __name__ == "__main__":
    {"fetch": fetch, "build": build}[sys.argv[1] if len(sys.argv) > 1 else "fetch"]()

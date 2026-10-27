#!/usr/bin/env python3
"""Fotos dos produtos do seed (#147): CC0 ou domínio público, do Openverse.

Dois passos, para a escolha das fotos ser revisada por uma pessoa e o resultado ser reproduzível:

  python3 scripts/media/product_images.py fetch   # baixa candidatas e monta uma prancha por subcategoria
  # revisar .cache/media/<sub>/prancha.jpg e anotar os ids escolhidos em scripts/media/selection.json
  python3 scripts/media/product_images.py build   # gera infra/media/produtos/*.webp e os créditos

Cada foto vira um quadrado (recorte central) com variantes WebP por largura (VARIANT_WIDTHS), nunca
maiores que a original: o front escolhe pelo srcset a que serve para a tela. O nome leva o hash do
conteúdo (tv-1.3fa9c2d1-640.webp), então a CDN guarda cada arquivo para sempre (immutable). O
manifesto (infra/media/manifest.json) diz as variantes de cada foto; o seed o copia (go generate).
Precisa de Pillow (pip install pillow).
"""
import hashlib
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
MANIFEST = ROOT / "infra" / "media" / "manifest.json"

# larguras do srcset: card da vitrine (1x/2x) até a página do produto em tela retina
VARIANT_WIDTHS = (320, 480, 640, 960, 1200)
MIN_SIZE = 600  # foto menor que isso não entra: ficaria borrada na página do produto
SIZE = 800  # das pranchas de escolha
CANDIDATES = 20  # por busca (o máximo do Openverse sem chave)
API = "https://api.openverse.org/v1/images/"
# fontes de fotografia CC0/domínio público; a Wikimedia traz muito logo e diagrama
SOURCES = "stocksnap,rawpixel"
USER_AGENT = "compra-facil-seed/1.0 (https://github.com/mateus-vitor-ferreira-dev/compra-facil)"

# subcategoria do seed (backend/services/catalog/cmd/seed/catalog_data.go) -> buscas em inglês. Várias
# por subcategoria (#178): com uma só, as candidatas boas não davam 10 fotos diferentes
QUERIES = {
    "tv": ["television", "flat screen tv", "tv living room"],
    "fones": ["headphones", "wireless earbuds", "headset"],
    "caixas-de-som": ["speaker", "bluetooth speaker", "soundbar"],
    "notebooks": ["laptop", "notebook computer", "laptop desk"],
    "monitores": ["computer monitor", "desktop computer screen", "monitor desk"],
    "perifericos": ["keyboard", "computer mouse", "webcam"],
    "geladeiras": ["refrigerator", "fridge kitchen", "refrigerator door"],
    "fogoes": ["stove", "cooktop", "gas stove kitchen"],
    "lavadoras": ["washing machine", "laundry machine", "front load washer"],
    "sofas": ["sofa", "couch living room", "armchair"],
    "colchoes": ["bed", "mattress", "bedroom bed"],
    "bicicletas": ["bicycle", "mountain bike", "road bike"],
    "fitness": ["dumbbell", "treadmill", "kettlebell"],
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


# a URL do Openverse é a maior versão sem marca d'água: as maiores do rawpixel (image_1300) têm marca
# e as do StockSnap param em 960 px de largura
def original(path):
    return ImageOps.exif_transpose(Image.open(path)).convert("RGB")


def square(path, size=SIZE):
    image = original(path)
    return ImageOps.fit(image, (size, size), Image.Resampling.LANCZOS, centering=(0.5, 0.5))



def fetch_one(sub, queries):
    folder = CACHE / sub
    folder.mkdir(parents=True, exist_ok=True)
    meta_file = folder / "meta.json"
    # o cache acumula: as fotos já escolhidas em buscas anteriores continuam achadas pelo build
    kept = json.loads(meta_file.read_text()) if meta_file.exists() else []
    seen = {r["id"] for r in kept}
    results = [r for q in queries for r in search(q)]
    for r in results:
        if r["id"] in seen:
            continue
        seen.add(r["id"])
        path = folder / f"{r['id']}.img"
        try:
            if not path.exists():
                path.write_bytes(get(r["url"]))
            square(path)  # descarta o que não abre
            kept.append(r)
        except Exception as e:
            print(f"  {sub} {r['id']}: {e}", file=sys.stderr)
    meta_file.write_text(json.dumps(kept, ensure_ascii=False, indent=1))
    sheet(sub, kept)
    print(f"{sub}: {len(kept)} candidatas")


def sheet(sub, results):
    thumb, cols = 200, 6
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


def find(short):
    """A candidata pelo id curto, em qualquer pasta do cache."""
    for meta_file in CACHE.glob("*/meta.json"):
        for r in json.loads(meta_file.read_text()):
            if r["id"].startswith(short):
                return r, meta_file.parent / f"{r['id']}.img"
    raise SystemExit(f"{short}: não está no cache; rode o fetch")


def build():
    selection = json.loads(SELECTION.read_text())
    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.webp"):
        old.unlink()
    lines = [
        "# Créditos das fotos de produto",
        "",
        "Fotos em domínio público ou CC0, encontradas pelo [Openverse](https://openverse.org) e convertidas em",
        "variantes WebP quadradas (recorte central) por `scripts/media/product_images.py`. Não é exigida",
        "atribuição; os créditos ficam aqui por transparência. As marcas citadas nos nomes dos produtos do seed",
        "são só massa de teste: as fotos não mostram esses produtos nem têm relação com essas marcas.",
        "",
        "| Foto | Maior variante | Título | Autor | Licença | Origem |",
        "|---|---|---|---|---|---|",
    ]
    manifest = {}
    for sub, ids in selection.items():
        for n, short in enumerate(ids, start=1):
            r, path = find(short)
            side = min(original(path).size)
            if side < MIN_SIZE:
                raise SystemExit(f"{sub} {short}: original com {side}px, menos que {MIN_SIZE}; escolha outra")
            top = min(side, max(VARIANT_WIDTHS))
            # largura padrão colada na da original (640 de uma de 683) não vale o arquivo a mais
            widths = sorted({w for w in VARIANT_WIDTHS if w <= top * 0.85} | {top})
            base = square(path, top)
            digest = hashlib.sha256(base.tobytes()).hexdigest()[:8]
            name = f"{sub}-{n}"
            variants = []
            for w in widths:
                file = f"{name}.{digest}-{w}.webp"
                (base if w == top else base.resize((w, w), Image.Resampling.LANCZOS)).save(
                    OUT / file, "WEBP", quality=80, method=6)
                variants.append({"width": w, "file": file})
            manifest[name] = {"width": top, "height": top, "variants": variants}
            license_ = f"{r['license'].upper()} {r.get('license_version') or ''}".strip()
            title = (r.get("title") or "").replace("|", "/")
            creator = (r.get("creator") or "—").replace("|", "/")
            lines.append(f"| `{name}` | {top}px | {title} | {creator} | {license_} | [{r['source']}]({r['foreign_landing_url']}) |")
    CREDITS.write_text("\n".join(lines) + "\n")
    MANIFEST.write_text(json.dumps(manifest, indent=1) + "\n")
    files = list(OUT.glob("*.webp"))
    size = sum(p.stat().st_size for p in files) / 1024
    print(f"{len(manifest)} fotos, {len(files)} variantes em {OUT.relative_to(ROOT)} ({size:.0f} KiB); "
          f"manifesto em {MANIFEST.relative_to(ROOT)}")


if __name__ == "__main__":
    {"fetch": fetch, "build": build}[sys.argv[1] if len(sys.argv) > 1 else "fetch"]()

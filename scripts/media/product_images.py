#!/usr/bin/env python3
"""Fotos dos produtos do seed (#147, #178): licença livre, escolhidas pelo agente de fotos.

Quem procura e escolhe é o agente de fotos do marketing-crew (busca no Wikimedia Commons e no Openverse,
só licenças livres, e o Claude dá nota a cada candidata olhando a foto). Este script faz o pedido,
importa a escolha e gera os arquivos da loja:

  python3 scripts/media/product_images.py pedido            # .cache/media/pedido.json
  # se alguma subcategoria ficar curta: pedido .cache/media/resultado.json pede só o que falta
  ai-fotos .cache/media/pedido.json -o .cache/media/resultado.json   # o agente (ver o README dele)
  python3 scripts/media/product_images.py importar .cache/media/resultado.json [outra-rodada.json]
  # revisar .cache/media/<sub>/prancha.jpg; tirar de scripts/media/selection.json o que não servir
  python3 scripts/media/product_images.py build             # infra/media/produtos/*.webp e os créditos

Cada foto vira um quadrado (recorte central) com variantes WebP por largura (VARIANT_WIDTHS), nunca
maiores que a original: o front escolhe pelo srcset a que serve para a tela. O nome leva o hash do
conteúdo (tv-1.3fa9c2d1-640.webp), então a CDN guarda cada arquivo para sempre (immutable). O
manifesto (infra/media/manifest.json) diz as variantes de cada foto; o seed o copia (go generate).
Precisa de Pillow (pip install pillow).
"""
import hashlib
import json
import re
import sys
import time
import urllib.error
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
PER_SUB = 8  # por tipo; o seed pede pelo menos minPhotosPerSubcategory
USER_AGENT = "compra-facil-seed/1.0 (https://github.com/mateus-vitor-ferreira-dev/compra-facil)"
# CC BY e CC BY-SA pedem o crédito, que fica no CREDITOS.md (linkado no README); a licença do Pixabay não
# pede, mas o crédito fica lá do mesmo jeito
LICENSES = ["cc0", "pd", "cc-by", "cc-by-sa", "pixabay"]
SOURCES = ["pixabay", "commons", "openverse"]

# tipo de produto do seed (o último nível de backend/services/catalog/cmd/seed/catalog_data.go, #210) -> o
# que é, para o curador, e as buscas em inglês. As fotos são por tipo: o filtro "Cadeira gamer" não pode
# mostrar foto de mouse
SUBCATEGORIES = {
    # Eletrônicos
    "smart-tvs": ("smart TVs de tela plana, o aparelho inteiro", ["flat screen tv", "smart tv", "tv living room", "led television"]),
    "projetores": ("projetores de vídeo domésticos", ["video projector", "home projector", "projector"]),
    "suportes-de-tv": ("suportes de parede e pedestais para TV", ["tv wall mount", "tv stand", "tv bracket"]),
    "fones-over-ear": ("fones de ouvido grandes (headphones over-ear)", ["headphones", "over ear headphones", "wireless headphones"]),
    "fones-sem-fio": ("fones intra-auriculares sem fio (earbuds)", ["wireless earbuds", "earbuds case", "true wireless earphones"]),
    "headsets-gamer": ("headsets gamer com microfone", ["gaming headset", "headset microphone", "gamer headphones"]),
    "caixas-bluetooth": ("caixas de som portáteis bluetooth", ["bluetooth speaker", "portable speaker", "wireless speaker"]),
    "soundbars": ("soundbars para TV", ["soundbar", "sound bar tv", "home theater soundbar"]),
    "smart-speakers": ("caixas de som inteligentes (assistente de voz)", ["smart speaker", "voice assistant speaker", "smart home speaker"]),
    # Informática
    "notebooks-e-ultrabooks": ("notebooks (laptops)", ["laptop", "notebook computer", "laptop desk"]),
    "tablets": ("tablets", ["tablet computer", "tablet device", "tablet stylus"]),
    "monitores-pc": ("monitores de computador", ["computer monitor", "monitor desk", "desktop monitor"]),
    "mouses": ("mouses de computador", ["computer mouse", "wireless mouse", "gaming mouse"]),
    "teclados": ("teclados de computador", ["computer keyboard", "mechanical keyboard", "wireless keyboard"]),
    "mousepads": ("mousepads", ["mousepad", "mouse pad desk", "gaming mouse pad"]),
    "cadeiras-gamer": ("cadeiras gamer e de escritório", ["gaming chair", "office chair", "ergonomic chair"]),
    "webcams": ("webcams", ["webcam", "web camera", "usb camera computer"]),
    # Eletrodomésticos
    "geladeiras-duplex": ("geladeiras domésticas fechadas", ["refrigerator", "fridge kitchen", "stainless steel refrigerator"]),
    "freezers": ("freezers horizontais e verticais", ["chest freezer", "upright freezer", "freezer appliance"]),
    "frigobares": ("frigobares (geladeiras pequenas)", ["mini fridge", "compact refrigerator", "small fridge"]),
    "fogoes-de-piso": ("fogões de piso com forno", ["stove oven", "gas range", "kitchen stove"]),
    "cooktops": ("cooktops de embutir (gás ou indução)", ["cooktop", "induction cooktop", "gas hob"]),
    "fornos-eletricos": ("fornos elétricos e micro-ondas", ["electric oven", "microwave oven", "toaster oven"]),
    "lavadoras-de-roupa": ("máquinas de lavar roupa", ["washing machine", "front load washer", "laundry machine"]),
    "lava-e-seca": ("lava e seca e secadoras de roupa", ["washer dryer", "clothes dryer", "tumble dryer"]),
    # Casa e móveis
    "sofas-retrateis": ("sofás", ["sofa", "couch living room", "modern sofa"]),
    "poltronas": ("poltronas", ["armchair", "accent chair", "lounge chair"]),
    "puffs": ("puffs e banquetas estofadas", ["pouf", "ottoman", "footstool"]),
    "colchoes-de-molas": ("colchões", ["mattress", "mattress bedroom", "foam mattress"]),
    "camas-box": ("camas box e camas de casal", ["bed bedroom", "double bed", "box spring bed"]),
    "travesseiros": ("travesseiros", ["pillow", "bed pillows", "white pillow"]),
    # Esporte e lazer
    "bicicletas-urbanas": ("bicicletas", ["bicycle", "city bike", "mountain bike"]),
    "bicicletas-eletricas": ("bicicletas elétricas", ["electric bicycle", "e-bike", "electric bike"]),
    "capacetes": ("capacetes de ciclismo", ["bicycle helmet", "cycling helmet", "bike helmet"]),
    "halteres": ("halteres e anilhas", ["dumbbell", "dumbbells gym", "weight plates"]),
    "esteiras": ("esteiras ergométricas", ["treadmill", "treadmill gym", "running machine"]),
    "kettlebells": ("kettlebells", ["kettlebell", "kettlebell workout", "kettlebells gym"]),
    "tapetes-de-yoga": ("tapetes de yoga", ["yoga mat", "exercise mat", "rolled yoga mat"]),
    "bicicletas-ergometricas": ("bicicletas ergométricas e spinning", ["exercise bike", "stationary bike", "spin bike"]),
    # Beleza e cuidados
    "batons": ("batons", ["lipstick", "lipsticks", "lipstick makeup"]),
    "bases-e-corretivos": ("bases e corretivos de maquiagem", ["foundation makeup bottle", "liquid foundation", "concealer makeup"]),
    "paletas-de-sombra": ("paletas de sombra", ["eyeshadow palette", "eye shadow", "makeup palette"]),
    "mascaras-de-cilios": ("máscaras de cílios (rímel)", ["mascara", "mascara wand", "mascara makeup"]),
    "hidratantes": ("hidratantes faciais e corporais", ["face cream jar", "moisturizer", "body lotion bottle"]),
    "protetores-solares": ("protetores solares", ["sunscreen bottle", "sunscreen", "sun cream"]),
    "sabonetes-faciais": ("sabonetes e limpadores faciais", ["facial cleanser", "face wash", "soap bar"]),
    "perfumes": ("perfumes (frascos)", ["perfume bottle", "perfume", "fragrance bottle"]),
    "shampoos": ("shampoos e condicionadores", ["shampoo bottle", "shampoo", "hair conditioner"]),
    "secadores-de-cabelo": ("secadores de cabelo", ["hair dryer", "hairdryer", "blow dryer"]),
    "chapinhas": ("chapinhas e babyliss", ["hair straightener", "flat iron hair", "curling iron"]),
    "barbeadores": ("barbeadores e lâminas", ["razor", "shaving razor", "safety razor"]),
    "aparadores": ("aparadores e máquinas de cortar cabelo", ["beard trimmer", "hair clipper", "electric trimmer"]),
    # Moda e acessórios
    "tenis": ("tênis", ["sneakers", "running shoes", "white sneakers"]),
    "botas": ("botas", ["boots", "leather boots", "ankle boots"]),
    "sandalias": ("sandálias e chinelos", ["sandals", "flip flops", "summer sandals"]),
    "mochilas": ("mochilas", ["backpack", "laptop backpack", "travel backpack"]),
    "bolsas": ("bolsas", ["handbag", "leather bag", "tote bag"]),
    "malas-de-viagem": ("malas de viagem", ["suitcase", "luggage", "travel suitcase"]),
    "relogios-analogicos": ("relógios de pulso", ["wrist watch", "analog watch", "watch on wrist"]),
    "smartwatches": ("smartwatches", ["smartwatch", "smart watch wrist", "fitness tracker"]),
    "oculos-de-sol": ("óculos de sol", ["sunglasses", "sunglasses fashion", "aviator sunglasses"]),
    "bones": ("bonés", ["baseball cap", "cap hat", "snapback cap"]),
    "carteiras": ("carteiras", ["wallet", "leather wallet", "mens wallet"]),
    "cintos": ("cintos", ["belt", "leather belt", "belt buckle"]),
    # Casa e cozinha
    "panelas": ("panelas e jogos de panelas", ["cooking pot", "cookware set", "saucepan"]),
    "frigideiras": ("frigideiras", ["frying pan", "skillet", "non stick pan"]),
    "air-fryers": ("air fryers (fritadeiras elétricas)", ["air fryer", "airfryer", "air fryer kitchen"]),
    "cafeteiras": ("cafeteiras e máquinas de café", ["coffee maker", "espresso machine", "coffee machine"]),
    "liquidificadores": ("liquidificadores", ["blender", "kitchen blender", "smoothie blender"]),
    "batedeiras": ("batedeiras", ["stand mixer", "kitchen mixer", "hand mixer"]),
    "sanduicheiras": ("sanduicheiras e grills", ["sandwich maker", "panini press", "toaster"]),
    "facas": ("facas de cozinha", ["kitchen knife", "chef knife", "knife set"]),
    "tabuas": ("tábuas de corte", ["cutting board", "wooden cutting board", "chopping board"]),
    "potes": ("potes e organizadores de cozinha", ["food storage containers", "glass jars kitchen", "kitchen containers"]),
    # Games
    "consoles": ("consoles de videogame", ["video game console", "game console", "gaming console"]),
    "controles": ("controles de videogame", ["game controller", "gamepad", "video game controller"]),
    "volantes-e-acessorios": ("volantes e acessórios gamer", ["racing wheel gaming", "gaming accessories", "steering wheel controller"]),
    # Brinquedos
    "blocos-de-montar": ("blocos de montar", ["building blocks toy", "toy bricks", "construction toy"]),
    "jogos-de-tabuleiro": ("jogos de tabuleiro", ["board game", "chess board", "board game pieces"]),
    "pelucias": ("bichos de pelúcia", ["teddy bear", "plush toy", "stuffed animal"]),
    "carrinhos": ("carrinhos de brinquedo", ["toy car", "toy cars", "model car toy"]),
    # Pets
    "camas-pet": ("camas para cachorro e gato", ["dog bed", "pet bed", "cat bed"]),
    "brinquedos-pet": ("brinquedos para pets", ["dog toy", "pet toy", "cat toy"]),
    "coleiras": ("coleiras e guias", ["dog collar", "dog leash", "pet collar"]),
    "comedouros": ("comedouros e bebedouros", ["dog bowl", "pet bowl", "cat bowl"]),
}


def get(url, timeout=60):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                return response.read()
        except urllib.error.HTTPError as e:
            # o Pixabay limita downloads seguidos (429): espera e tenta de novo
            if e.code != 429 or attempt == 4:
                raise
            time.sleep(int(e.headers.get("Retry-After") or 10 * (attempt + 1)))


def original(path):
    return ImageOps.exif_transpose(Image.open(path)).convert("RGB")


def square(path, size=SIZE):
    image = original(path)
    return ImageOps.fit(image, (size, size), Image.Resampling.LANCZOS, centering=(0.5, 0.5))


def safe(photo_id):
    """O id da foto ("commons:123") como nome de arquivo."""
    return re.sub(r"[^a-zA-Z0-9_-]", "_", photo_id)


def pedido(*anteriores):
    """O pedido para o agente de fotos. Com resultados anteriores, pede só as subcategorias que ainda não
    têm PER_SUB fotos, sem as fontes já usadas (uma rodada a mais, para juntar no importar)."""
    CACHE.mkdir(parents=True, exist_ok=True)
    tem, fontes_usadas = {}, set()
    for path in anteriores:
        for sub, dados in json.loads(Path(path).read_text())["subcategorias"].items():
            tem[sub] = tem.get(sub, 0) + len(dados["escolhidas"])
            fontes_usadas |= {f["fonte"] for f in dados["escolhidas"]}
    faltam = {sub: v for sub, v in SUBCATEGORIES.items() if tem.get(sub, 0) < PER_SUB}
    fontes = [f for f in SOURCES if not anteriores or f == "pixabay" or f not in fontes_usadas]
    out = CACHE / ("pedido.json" if not anteriores else "pedido-complemento.json")
    out.write_text(json.dumps({
        "projeto": "compra-facil",
        "licencas": LICENSES,
        "fontes": fontes,
        "lado_minimo": MIN_SIZE,
        "nota_minima": 7,
        "max_candidatas": 80,
        "paralelas": 4,
        "subcategorias": {sub: {"descricao": desc, "buscas": queries, "quantas": PER_SUB}
                          for sub, (desc, queries) in faltam.items()},
    }, ensure_ascii=False, indent=1))
    print(f"{out.relative_to(ROOT)}: {len(faltam)} subcategorias, fontes {', '.join(fontes)}; rode o agente de fotos com ele (ai-fotos)")


def importar(*resultado_paths):
    """Baixa as fotos escolhidas pelo agente, monta as pranchas e grava o selection.json. Com mais de um
    resultado (uma rodada a mais para as subcategorias que ficaram curtas), junta as escolhidas, as de
    nota maior primeiro, até PER_SUB."""
    if not resultado_paths:
        raise SystemExit("uso: product_images.py importar <resultado.json> [<outro.json> ...]")
    juntas = {}
    for path in resultado_paths:
        for sub, dados in json.loads(Path(path).read_text())["subcategorias"].items():
            por_id = juntas.setdefault(sub, {})
            for foto in dados["escolhidas"]:
                por_id.setdefault(foto["id"], foto)
    resultado = {"subcategorias": {
        sub: {"escolhidas": sorted(por_id.values(), key=lambda f: -f["avaliacao"]["nota"])[:PER_SUB]}
        for sub, por_id in juntas.items()}}
    selection = {}

    def baixa(item):
        sub, foto = item
        path = CACHE / sub / f"{safe(foto['id'])}.img"
        if not path.exists():
            # a mesma foto pode já estar no cache de outra subcategoria (de uma seleção anterior)
            antiga = next((p for p in CACHE.glob(f"*/{safe(foto['id'])}.img") if p.stat().st_size), None)
            path.write_bytes(antiga.read_bytes() if antiga else get(foto["url"]))
        square(path)  # descarta o que não abre
        return foto

    for sub, dados in resultado["subcategorias"].items():
        (CACHE / sub).mkdir(parents=True, exist_ok=True)
        with ThreadPoolExecutor(max_workers=2) as pool:
            fotos = []
            for foto in pool.map(lambda f: _tenta(baixa, (sub, f)), dados["escolhidas"]):
                if foto:
                    fotos.append(foto)
        (CACHE / sub / "meta.json").write_text(json.dumps(fotos, ensure_ascii=False, indent=1))
        sheet(sub, fotos)
        selection[sub] = [f["id"] for f in fotos]
        aviso = "" if len(fotos) >= 10 else "  <- menos de 10: o seed recusa; rode o agente com mais buscas"
        print(f"{sub}: {len(fotos)} fotos{aviso}")
    SELECTION.write_text(json.dumps(selection, indent=1) + "\n")
    print(f"{SELECTION.relative_to(ROOT)} gravado; revise as pranchas em .cache/media/<sub>/prancha.jpg")


def _tenta(fn, arg):
    try:
        return fn(arg)
    except Exception as e:
        print(f"  {arg[0]} {arg[1]['id']}: {e}", file=sys.stderr)
        return None


def sheet(sub, fotos):
    thumb, cols = 200, 6
    rows = (len(fotos) + cols - 1) // cols
    board = Image.new("RGB", (cols * thumb, max(rows, 1) * (thumb + 22)), "white")
    draw = ImageDraw.Draw(board)
    for i, f in enumerate(fotos):
        x, y = (i % cols) * thumb, (i // cols) * (thumb + 22)
        board.paste(square(CACHE / sub / f"{safe(f['id'])}.img").resize((thumb - 6, thumb - 6)), (x + 3, y + 3))
        draw.text((x + 4, y + thumb), f"{i}: {f['id']} ({f['avaliacao']['nota']})", fill="black")
    board.save(CACHE / sub / "prancha.jpg", quality=80)


def find(sub, photo_id):
    meta_file = CACHE / sub / "meta.json"
    for f in json.loads(meta_file.read_text()) if meta_file.exists() else []:
        if f["id"] == photo_id:
            return f, CACHE / sub / f"{safe(photo_id)}.img"
    raise SystemExit(f"{sub} {photo_id}: não está no cache; rode o importar")


def cell(text):
    return (text or "—").replace("|", "/").replace("\n", " ").strip()


def build():
    selection = json.loads(SELECTION.read_text())
    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.webp"):
        old.unlink()
    lines = [
        "# Créditos das fotos de produto",
        "",
        "Fotos com licença livre, do [Pixabay](https://pixabay.com), do [Wikimedia Commons](https://commons.wikimedia.org)",
        "e do [Openverse](https://openverse.org), escolhidas pelo agente de fotos (o Claude avalia cada candidata) e",
        "revisadas, e convertidas em variantes WebP quadradas (recorte central) por `scripts/media/product_images.py`.",
        "",
        "As fotos CC BY e CC BY-SA pedem o crédito abaixo (autor, licença e origem). As variantes WebP de uma foto",
        "CC BY-SA são obras derivadas e ficam sob a mesma licença dela. A [licença do Pixabay](https://pixabay.com/service/license-summary/)",
        "permite usar e editar sem atribuição; o crédito fica aqui do mesmo jeito. As marcas citadas nos nomes dos",
        "produtos do seed são só massa de teste: as fotos não mostram esses produtos nem têm relação com essas marcas.",
        "",
        "Onde os acervos livres não tinham fotos de catálogo suficientes, as que faltavam foram geradas por IA (Runway",
        "e Figma), sem marca, e estão marcadas assim na tabela. Os tipos de produto ainda sem foto mostram o ícone da",
        "categoria na loja.",
        "",
        "| Foto | Maior variante | Título | Autor | Licença | Origem |",
        "|---|---|---|---|---|---|",
    ]
    manifest = {}
    for sub, ids in selection.items():
        for n, photo_id in enumerate(ids, start=1):
            f, path = find(sub, photo_id)
            side = min(original(path).size)
            if side < MIN_SIZE:
                raise SystemExit(f"{sub} {photo_id}: original com {side}px, menos que {MIN_SIZE}; escolha outra")
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
            license_ = cell(f.get("licenca_nome"))
            if f.get("licenca_url"):
                license_ = f"[{license_}]({f['licenca_url']})"
            origem = f"[{f['fonte']}]({f['origem']})" if f.get("origem") else cell(f["fonte"])
            lines.append(f"| `{name}` | {top}px | {cell(f['titulo'])} | {cell(f['autor'])} | {license_} | {origem} |")
    CREDITS.write_text("\n".join(lines) + "\n")
    MANIFEST.write_text(json.dumps(manifest, indent=1) + "\n")
    files = list(OUT.glob("*.webp"))
    size = sum(p.stat().st_size for p in files) / 1024
    print(f"{len(manifest)} fotos, {len(files)} variantes em {OUT.relative_to(ROOT)} ({size:.0f} KiB); "
          f"manifesto em {MANIFEST.relative_to(ROOT)}")


if __name__ == "__main__":
    comandos = {"pedido": pedido, "importar": importar, "build": build}
    if len(sys.argv) < 2 or sys.argv[1] not in comandos:
        raise SystemExit(f"uso: {sys.argv[0]} pedido | importar <resultado.json> | build")
    comandos[sys.argv[1]](*sys.argv[2:])

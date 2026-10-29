"""Собирает docs/demo.gif (кадры окна IcoSwap) и docs/icons.gif (анимация «было → стало»).

python make_gifs.py --frames <папка с кадрами> --sys <png системных значков> --pack <previews пака> --out docs
"""
import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

BG = (24, 24, 24)


def font(size, bold=False):
    name = "segoeuib.ttf" if bold else "segoeui.ttf"
    try:
        return ImageFont.truetype(name, size)
    except OSError:
        return ImageFont.load_default()


def fix_mask(img):
    """Закрашивает прямоугольник, которым инструмент съёмки замаскировал чужое окно в правом верхнем углу."""
    img = img.convert("RGB")
    px = img.load()
    w, h = img.size
    x0 = int(w * 0.935)
    for y in range(0, int(h * 0.078)):
        c = px[x0 - 4, y]
        for x in range(x0, w):
            px[x, y] = c
    return img


def crossfade(a, b, steps):
    return [Image.blend(a, b, (i + 1) / (steps + 1)) for i in range(steps)]


def save_gif(frames, durations, path):
    pal = [f.convert("P", palette=Image.ADAPTIVE, colors=255, dither=Image.NONE) for f in frames]
    pal[0].save(path, save_all=True, append_images=pal[1:], duration=durations, loop=0, optimize=True, disposal=1)
    print(f"{path}: {len(frames)} кадров, {Path(path).stat().st_size // 1024} КБ")


def make_demo(frame_dir, out, width=860):
    names = sorted(p for p in Path(frame_dir).glob("*.jpg"))
    shots = [fix_mask(Image.open(p)) for p in names]
    h = round(shots[0].height * width / shots[0].width)
    shots = [s.resize((width, h), Image.LANCZOS) for s in shots]
    holds = [1400, 2200, 2000, 2000, 1600, 700, 2200, 2000, 1400, 2400]
    frames, durations = [], []
    for i, s in enumerate(shots):
        frames.append(s)
        durations.append(holds[i] if i < len(holds) else 1600)
        nxt = shots[(i + 1) % len(shots)]
        for f in crossfade(s, nxt, 3):
            frames.append(f)
            durations.append(60)
    save_gif(frames, durations, out)


PAIRS = [
    ("thispc", "this-pc", "Этот компьютер"),
    ("recycle", "recycle-bin-empty", "Корзина"),
    ("network", "network", "Сеть"),
    ("folder", "folder", "Папки"),
    ("downloads", "Загрузки", "Загрузки"),
    ("pictures", "pictures", "Изображения"),
    ("txt", "txt", ".txt"),
    ("zip", "zip", ".zip"),
]


def tile_sheet(icon_dir_before, icon_dir_after, t):
    """t=0 — системные значки, t=1 — значки пака; между ними плавный переход."""
    cols, cell_w, cell_h, icon = 4, 190, 170, 96
    w, h = cols * cell_w + 40, 2 * cell_h + 110
    img = Image.new("RGB", (w, h), BG)
    d = ImageDraw.Draw(img)
    title = "Было" if t < 0.5 else "Стало"
    d.text((20, 18), "IcoSwap", font=font(30, True), fill=(240, 240, 240))
    d.text((w - 20, 26), title, font=font(24), fill=(150, 150, 150) if t < 0.5 else (120, 200, 120), anchor="ra")
    for i, (before, after, label) in enumerate(PAIRS):
        cx = 20 + (i % cols) * cell_w + cell_w // 2
        cy = 80 + (i // cols) * cell_h
        d.rounded_rectangle((cx - 80, cy, cx + 80, cy + 150), radius=14, fill=(38, 38, 38))
        a = Image.open(Path(icon_dir_before) / f"{before}.png").convert("RGBA").resize((icon, icon), Image.LANCZOS)
        b = Image.open(Path(icon_dir_after) / f"{after}.png").convert("RGBA").resize((icon, icon), Image.LANCZOS)
        mix = Image.blend(a, b, t)
        img.paste(mix, (cx - icon // 2, cy + 14), mix)
        d.text((cx, cy + 124), label, font=font(17), fill=(220, 220, 220), anchor="ma")
    return img


def make_icons(sys_dir, pack_dir, out):
    frames, durations = [], []
    steps = 12
    for t in [0] + [i / steps for i in range(1, steps)] + [1]:
        frames.append(tile_sheet(sys_dir, pack_dir, t))
        durations.append(1600 if t in (0, 1) else 50)
    for t in [i / steps for i in range(steps - 1, 0, -1)]:
        frames.append(tile_sheet(sys_dir, pack_dir, t))
        durations.append(50)
    save_gif(frames, durations, out)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--frames", required=True)
    ap.add_argument("--sys", required=True)
    ap.add_argument("--pack", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    Path(a.out).mkdir(parents=True, exist_ok=True)
    make_demo(a.frames, str(Path(a.out) / "demo.gif"))
    make_icons(a.sys, a.pack, str(Path(a.out) / "icons.gif"))

"""Draws the Setup wizard sidebar (finish page): brand gradient, app icon and name.

Run from the repository root: python scripts/make-installer-art.py
Writes installer/wizard-<width>.png at the sizes Inno Setup picks from for each DPI.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ICON = ROOT / 'src' / 'WinUI' / 'Assets' / 'Locaptio.png'
FONT = Path(r'C:\Windows\Fonts\seguisb.ttf')
# The icon's sound bars, top to bottom: soft green, teal, navy.
STOPS = [(0.0, (138, 198, 160)), (0.5, (54, 131, 146)), (1.0, (12, 62, 108))]
BASE = (164, 314)  # Inno Setup modern wizard image at 100% scaling
SCALES = (1, 1.5, 2, 2.5, 3)


def color_at(t):
    for (t0, c0), (t1, c1) in zip(STOPS, STOPS[1:]):
        if t <= t1:
            f = (t - t0) / (t1 - t0)
            return tuple(round(a + (b - a) * f) for a, b in zip(c0, c1))
    return STOPS[-1][1]


def draw(width, height):
    image = Image.new('RGB', (width, height))
    pixels = ImageDraw.Draw(image)
    for y in range(height):
        pixels.line([(0, y), (width, y)], fill=color_at(y / (height - 1)))
    icon_size = round(width * 0.52)
    icon = Image.open(ICON).convert('RGBA').resize((icon_size, icon_size), Image.LANCZOS)
    # A soft shadow lifts the light icon off the gradient.
    shadow = Image.new('RGBA', image.size, (0, 0, 0, 0))
    left, top = (width - icon_size) // 2, round(height * 0.30)
    alpha = icon.getchannel('A').point(lambda a: a * 0.35)
    shadow.paste((0, 0, 0, 255), (left, top + round(icon_size * 0.04)), alpha)
    shadow = shadow.filter(ImageFilter.GaussianBlur(icon_size * 0.04))
    image = Image.alpha_composite(image.convert('RGBA'), shadow)
    image.alpha_composite(icon, (left, top))
    text = ImageDraw.Draw(image)
    font = ImageFont.truetype(str(FONT), round(width * 0.155))
    box = text.textbbox((0, 0), 'Locaptio', font=font)
    text.text(((width - (box[2] - box[0])) / 2 - box[0], top + icon_size + round(height * 0.07)), 'Locaptio', font=font, fill=(255, 255, 255))
    return image.convert('RGB')


for scale in SCALES:
    w, h = round(BASE[0] * scale), round(BASE[1] * scale)
    out = ROOT / 'installer' / f'wizard-{w}.png'
    draw(w, h).save(out, optimize=True)
    print(out.name, w, h)

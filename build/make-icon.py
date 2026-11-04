"""Regenerates the application icon from the bundled Material Symbols font.

Usage: python build/make-icon.py
Writes src/MaterialDockerImage/Assets/Icon/app.ico (16, 20, 24, 32, 40, 48, 64, 128, 256 px) and app-256.png.
The glyph is deployed_code (U+F720) in the default seed colour on a dark rounded tile.
Requires Pillow. The outputs are committed, so a normal build does not run this script.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parent.parent
font_path = root / "src/MaterialDockerImage/Assets/Fonts/MaterialSymbolsRoundedFilled.ttf"
out = root / "src/MaterialDockerImage/Assets/Icon"
out.mkdir(parents=True, exist_ok=True)

PRIMARY = (0x7F, 0xA8, 0xFF, 255)   # lightened tint of the default seed #3A6FD8, readable on the dark tile
TILE = (0x16, 0x19, 0x22, 255)
GLYPH = ""
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

def render(size: int) -> Image.Image:
    scale = 4
    big = size * scale
    image = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((0, 0, big - 1, big - 1), radius=int(big * 0.22), fill=TILE)
    font = ImageFont.truetype(str(font_path), int(big * 0.70))
    box = draw.textbbox((0, 0), GLYPH, font=font)
    x = (big - (box[2] - box[0])) / 2 - box[0]
    y = (big - (box[3] - box[1])) / 2 - box[1]
    draw.text((x, y), GLYPH, font=font, fill=PRIMARY)
    return image.resize((size, size), Image.LANCZOS)

frames = [render(s) for s in SIZES]
frames[-1].save(out / "app-256.png")
frames[-1].save(out / "app.ico", format="ICO", sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
print("wrote", out / "app.ico", out / "app-256.png")

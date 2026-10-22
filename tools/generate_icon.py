import argparse
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

CANVAS = 1024
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
OCEAN_TOP = (66, 133, 244)
OCEAN_BOTTOM = (21, 87, 176)
LINE = (255, 255, 255, 235)


def vertical_gradient(size: int, top: tuple, bottom: tuple) -> Image.Image:
    gradient = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(gradient)
    for y in range(size):
        t = y / (size - 1)
        draw.line([(0, y), (size, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
    return gradient


def draw_globe() -> Image.Image:
    margin = CANVAS * 0.04
    box = (margin, margin, CANVAS - margin, CANVAS - margin)
    radius = (CANVAS - 2 * margin) / 2
    center = CANVAS / 2
    line_width = round(CANVAS * 0.035)

    disc = Image.new("L", (CANVAS, CANVAS), 0)
    ImageDraw.Draw(disc).ellipse(box, fill=255)

    grid = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    draw = ImageDraw.Draw(grid)
    draw.line([(center, margin), (center, CANVAS - margin)], fill=LINE, width=line_width)
    draw.line([(margin, center), (CANVAS - margin, center)], fill=LINE, width=line_width)
    for half_width in (radius * 0.45, radius * 0.85):
        draw.ellipse((center - half_width, margin, center + half_width, CANVAS - margin), outline=LINE, width=line_width)
    for offset in (radius * 0.5, -radius * 0.5):
        chord = (radius**2 - offset**2) ** 0.5
        draw.line([(center - chord, center + offset), (center + chord, center + offset)], fill=LINE, width=line_width)
    draw.ellipse(box, outline=LINE, width=line_width)

    globe = vertical_gradient(CANVAS, OCEAN_TOP, OCEAN_BOTTOM)
    globe.alpha_composite(grid)
    globe.putalpha(ImageChops.multiply(globe.getchannel("A"), disc))
    return globe


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("target", type=Path, help="Pfad zur .ico-Datei")
    args = parser.parse_args()

    globe = draw_globe()
    globe.save(args.target, sizes=[(size, size) for size in ICO_SIZES])


if __name__ == "__main__":
    main()

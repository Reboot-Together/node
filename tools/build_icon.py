from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


def build_icon(source_path: Path, output_directory: Path) -> None:
    output_directory.mkdir(parents=True, exist_ok=True)
    source = Image.open(source_path).convert("L")
    side = min(source.size)
    left = (source.width - side) // 2
    top = (source.height - side) // 2
    source = source.crop((left, top, left + side, top + side)).resize((1024, 1024), Image.Resampling.LANCZOS)

    # Preserve antialiased symbol edges while removing generated lighting and texture.
    symbol_mask = source.point(lambda value: max(0, min(255, round((value - 145) * 3.4))))
    symbol_mask = symbol_mask.filter(ImageFilter.GaussianBlur(0.35))

    tile_mask = Image.new("L", (1024, 1024), 0)
    ImageDraw.Draw(tile_mask).rounded_rectangle((20, 20, 1004, 1004), radius=218, fill=255)
    symbol_mask = Image.composite(symbol_mask, Image.new("L", symbol_mask.size, 0), tile_mask)

    icon = Image.new("RGBA", (1024, 1024), (21, 21, 21, 0))
    icon.paste((21, 21, 21, 255), (0, 0), tile_mask)
    icon.paste((255, 255, 255, 255), (0, 0), symbol_mask)

    icon.save(output_directory / "NodeIcon.png", optimize=True)
    icon.resize((150, 150), Image.Resampling.LANCZOS).save(output_directory / "NodeIcon-150.png", optimize=True)
    icon.resize((44, 44), Image.Resampling.LANCZOS).save(output_directory / "NodeIcon-44.png", optimize=True)
    icon.save(
        output_directory / "Node.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Build reproducible Node Windows icon assets.")
    parser.add_argument("source", type=Path)
    parser.add_argument("output_directory", type=Path)
    arguments = parser.parse_args()
    build_icon(arguments.source, arguments.output_directory)

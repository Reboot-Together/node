from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


CANVAS_SIZE = 1024
SUPERSAMPLING = 4


def build_symbol() -> Image.Image:
    size = CANVAS_SIZE * SUPERSAMPLING
    icon = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(icon)

    nodes = {
        "top": (505, 122),
        "upper_left": (250, 270),
        "upper_right": (760, 245),
        "center": (500, 466),
        "left": (142, 548),
        "right": (850, 525),
        "lower_left": (310, 760),
        "lower_right": (690, 785),
        "bottom": (500, 914),
    }
    edges = (
        ("top", "upper_left"),
        ("top", "upper_right"),
        ("top", "center"),
        ("upper_left", "center"),
        ("upper_left", "left"),
        ("upper_left", "upper_right"),
        ("upper_right", "center"),
        ("upper_right", "right"),
        ("left", "center"),
        ("left", "lower_left"),
        ("center", "right"),
        ("center", "lower_left"),
        ("center", "lower_right"),
        ("right", "lower_right"),
        ("lower_left", "lower_right"),
        ("lower_left", "bottom"),
        ("lower_right", "bottom"),
    )

    def scaled(point: tuple[int, int]) -> tuple[int, int]:
        return point[0] * SUPERSAMPLING, point[1] * SUPERSAMPLING

    white = (255, 255, 255, 255)
    for source, target in edges:
        draw.line(
            (scaled(nodes[source]), scaled(nodes[target])),
            fill=white,
            width=38 * SUPERSAMPLING,
        )

    for name, point in nodes.items():
        radius = 59 if name == "center" else 47
        x, y = scaled(point)
        radius *= SUPERSAMPLING
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=white)

    return icon.resize((CANVAS_SIZE, CANVAS_SIZE), Image.Resampling.LANCZOS)


def build_icon(output_directory: Path) -> None:
    output_directory.mkdir(parents=True, exist_ok=True)
    icon = build_symbol()

    icon.save(output_directory / "NodeIconSource.png", optimize=True)
    icon.save(output_directory / "NodeIcon.png", optimize=True)
    icon.resize((150, 150), Image.Resampling.LANCZOS).save(
        output_directory / "NodeIcon-150.png", optimize=True
    )
    icon.resize((44, 44), Image.Resampling.LANCZOS).save(
        output_directory / "NodeIcon-44.png", optimize=True
    )
    icon.save(
        output_directory / "Node.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Build the transparent white Node network icon assets."
    )
    parser.add_argument("output_directory", type=Path)
    arguments = parser.parse_args()
    build_icon(arguments.output_directory)

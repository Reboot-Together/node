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

    background = (12, 27, 42, 255)
    starlight = (226, 236, 239, 255)
    muted_starlight = (165, 187, 198, 220)
    discovery = (211, 155, 75, 255)
    inset = 34 * SUPERSAMPLING
    draw.rounded_rectangle(
        (inset, inset, size - inset, size - inset),
        radius=154 * SUPERSAMPLING,
        fill=background,
    )

    nodes = {
        "alkaid": (154, 212),
        "mizar": (292, 252),
        "alioth": (406, 350),
        "megrez": (514, 466),
        "dubhe": (687, 505),
        "merak": (780, 688),
        "phecda": (594, 756),
    }
    edges = (
        ("alkaid", "mizar"),
        ("mizar", "alioth"),
        ("alioth", "megrez"),
        ("megrez", "dubhe"),
        ("dubhe", "merak"),
        ("merak", "phecda"),
        ("phecda", "megrez"),
    )

    def scaled(point: tuple[int, int]) -> tuple[int, int]:
        return point[0] * SUPERSAMPLING, point[1] * SUPERSAMPLING

    def four_point_star(point: tuple[int, int], outer: int, inner: int, color: tuple[int, int, int, int]) -> None:
        x, y = scaled(point)
        outer *= SUPERSAMPLING
        inner *= SUPERSAMPLING
        draw.polygon(
            (
                (x, y - outer),
                (x + inner, y - inner),
                (x + outer, y),
                (x + inner, y + inner),
                (x, y + outer),
                (x - inner, y + inner),
                (x - outer, y),
                (x - inner, y - inner),
            ),
            fill=color,
        )

    for point, radius in (
        ((226, 690), 12),
        ((332, 118), 9),
        ((856, 168), 7),
        ((870, 470), 8),
        ((420, 824), 7),
        ((116, 530), 6),
    ):
        x, y = scaled(point)
        radius *= SUPERSAMPLING
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=muted_starlight)
    four_point_star((304, 604), 27, 6, starlight)
    four_point_star((176, 454), 19, 5, muted_starlight)
    four_point_star((844, 314), 31, 7, discovery)
    four_point_star((718, 166), 15, 4, starlight)

    for source, target in edges:
        draw.line(
            (scaled(nodes[source]), scaled(nodes[target])),
            fill=starlight,
            width=34 * SUPERSAMPLING,
        )

    star_sizes = {
        "alkaid": 42,
        "mizar": 34,
        "alioth": 37,
        "megrez": 34,
        "dubhe": 45,
        "merak": 43,
        "phecda": 39,
    }
    for name, point in nodes.items():
        x, y = scaled(point)
        radius = star_sizes[name] * SUPERSAMPLING
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=starlight)
        core = max(11 * SUPERSAMPLING, int(radius * .48))
        draw.ellipse((x - core, y - core, x + core, y + core), fill=background)

    return icon.resize((CANVAS_SIZE, CANVAS_SIZE), Image.Resampling.LANCZOS)


def build_icon(output_directory: Path) -> None:
    output_directory.mkdir(parents=True, exist_ok=True)
    icon = build_symbol()

    icon.save(output_directory / "AsterismIconSource.png", optimize=True)
    icon.save(output_directory / "AsterismIcon.png", optimize=True)
    icon.resize((150, 150), Image.Resampling.LANCZOS).save(
        output_directory / "AsterismIcon-150.png", optimize=True
    )
    icon.resize((44, 44), Image.Resampling.LANCZOS).save(
        output_directory / "AsterismIcon-44.png", optimize=True
    )
    icon.save(
        output_directory / "Asterism.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Build the Asterism deep-space constellation icon assets."
    )
    parser.add_argument("output_directory", type=Path)
    arguments = parser.parse_args()
    build_icon(arguments.output_directory)

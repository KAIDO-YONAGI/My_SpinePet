from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw


@dataclass(frozen=True)
class AtlasRegion:
    name: str
    page: str
    x: int
    y: int
    width: int
    height: int


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Make selected Spine atlas regions transparent."
    )
    parser.add_argument("--atlas", required=True, type=Path)
    parser.add_argument("--texture", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--prefix", action="append", default=[])
    parser.add_argument("--name", action="append", default=[])
    return parser.parse_args()


def parse_bounds(value: str, region_name: str) -> tuple[int, int, int, int]:
    parts = [int(part.strip()) for part in value.split(",")]
    if len(parts) != 4:
        raise ValueError(f"Invalid bounds for atlas region '{region_name}'.")
    return parts[0], parts[1], parts[2], parts[3]


def read_regions(atlas_path: Path) -> list[AtlasRegion]:
    lines = atlas_path.read_text(encoding="utf-8-sig").splitlines()
    regions: list[AtlasRegion] = []
    page = ""
    index = 0

    while index < len(lines):
        candidate = lines[index].strip()
        if not candidate:
            index += 1
            continue
        if candidate.lower().endswith((".png", ".jpg", ".jpeg", ".webp")):
            page = candidate
            index += 1
            continue
        if ":" in candidate:
            index += 1
            continue

        region_name = candidate
        bounds: tuple[int, int, int, int] | None = None
        rotation = 0
        index += 1
        while index < len(lines):
            property_line = lines[index].strip()
            if not property_line:
                index += 1
                continue
            if ":" not in property_line:
                break

            key, value = property_line.split(":", maxsplit=1)
            if key.strip().lower() == "bounds":
                bounds = parse_bounds(value, region_name)
            elif key.strip().lower() == "rotate":
                rotation = int(value.strip())
            index += 1

        if not page:
            raise ValueError(f"Atlas region '{region_name}' has no page.")
        if bounds is None:
            raise ValueError(f"Atlas region '{region_name}' has no bounds.")
        x, y, width, height = bounds
        if rotation in (90, 270):
            width, height = height, width
        regions.append(AtlasRegion(region_name, page, x, y, width, height))

    return regions


def should_mask(
    region_name: str,
    prefixes: tuple[str, ...],
    names: set[str],
) -> bool:
    normalized = region_name.casefold()
    return normalized in names or any(
        normalized.startswith(prefix) for prefix in prefixes
    )


def main() -> None:
    args = parse_args()
    texture_name = args.texture.name.casefold()
    prefixes = tuple(prefix.casefold() for prefix in args.prefix)
    names = {name.casefold() for name in args.name}
    selected = [
        region
        for region in read_regions(args.atlas)
        if region.page.casefold() == texture_name
        and should_mask(region.name, prefixes, names)
    ]
    if not selected:
        raise RuntimeError("No atlas regions matched the requested filters.")
    selected_names = {region.name for region in selected}
    retained = [
        region
        for region in read_regions(args.atlas)
        if region.page.casefold() == texture_name
        and region.name not in selected_names
    ]

    with Image.open(args.texture) as source:
        image = source.convert("RGBA")
    removal_mask = Image.new("L", image.size, 0)
    mask_draw = ImageDraw.Draw(removal_mask)
    for region in selected:
        mask_draw.rectangle(
            (
                region.x,
                region.y,
                region.x + region.width - 1,
                region.y + region.height - 1,
            ),
            fill=255,
        )
    for region in retained:
        mask_draw.rectangle(
            (
                region.x,
                region.y,
                region.x + region.width - 1,
                region.y + region.height - 1,
            ),
            fill=0,
        )

    transparent = Image.new("RGBA", image.size, (0, 0, 0, 0))
    image.paste(transparent, (0, 0), removal_mask)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    image.save(args.output, format="PNG", optimize=True)
    masked_pixels = removal_mask.histogram()[255]
    print(
        f"masked_regions={len(selected)} "
        f"masked_pixels={masked_pixels} "
        f"output={args.output}"
    )


if __name__ == "__main__":
    main()

from __future__ import annotations

import argparse
import re
from pathlib import Path

import UnityPy


RESOURCE_ID_PATTERN = re.compile(r"^c\d+_[^_]+$", re.IGNORECASE)
RENDERABLE_TYPES = ("standing",)
RESOURCE_TYPES = (*RENDERABLE_TYPES, "icons")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract one NIKKE Spine resource set from a UnityFS bundle."
    )
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--resource-id", required=True)
    parser.add_argument(
        "--resource-type",
        required=True,
        choices=RESOURCE_TYPES,
    )
    parser.add_argument("--output-directory", required=True, type=Path)
    return parser.parse_args()


def text_asset_bytes(text_asset: object) -> bytes:
    payload = text_asset.m_Script
    if isinstance(payload, bytes):
        return payload
    return payload.encode("utf-8", errors="surrogateescape")


def get_atlas_pages(atlas_bytes: bytes) -> list[str]:
    atlas_text = atlas_bytes.decode("utf-8-sig", errors="strict")
    pages: list[str] = []
    for line in atlas_text.splitlines():
        candidate = line.strip()
        if not candidate.lower().endswith(".png"):
            continue
        if Path(candidate).name != candidate:
            raise RuntimeError(
                f"Atlas page paths with directories are unsupported: {candidate}"
            )
        if candidate not in pages:
            pages.append(candidate)
    if not pages:
        raise RuntimeError("The Spine atlas does not reference any PNG pages.")
    return pages


def validate_image(image: object, asset_name: str) -> None:
    if image.width <= 0 or image.height <= 0:
        raise RuntimeError(f"'{asset_name}' has invalid image dimensions.")


def extract_icon(
    environment: object,
    resource_id: str,
    output_directory: Path,
) -> None:
    sprite_name = f"mi_{resource_id}_s"
    matching_sprite = None
    for obj in environment.objects:
        if obj.type.name != "Sprite":
            continue
        sprite = obj.read()
        if sprite.m_Name.lower() == sprite_name:
            matching_sprite = sprite
            break

    if matching_sprite is None:
        raise RuntimeError(f"Sprite '{sprite_name}' was not found.")

    image = matching_sprite.image
    validate_image(image, f"Sprite '{matching_sprite.m_Name}'")
    output_directory.mkdir(parents=True, exist_ok=True)
    output_name = f"{resource_id}_icon.png"
    image.save(output_directory / output_name, format="PNG")
    print(f"Extracted {resource_id} icons: {output_name}")


def extract_renderable(
    environment: object,
    resource_id: str,
    resource_type: str,
    output_directory: Path,
) -> None:
    text_assets: dict[str, bytes] = {}
    textures: dict[str, object] = {}

    for obj in environment.objects:
        if obj.type.name == "TextAsset":
            asset = obj.read()
            text_assets[asset.m_Name.lower()] = text_asset_bytes(asset)
        elif obj.type.name == "Texture2D":
            texture = obj.read()
            textures[texture.m_Name.lower()] = texture

    skeleton_name = f"{resource_id}.skel"
    atlas_name = f"{resource_id}.atlas"
    if skeleton_name not in text_assets:
        raise RuntimeError(f"TextAsset '{skeleton_name}' was not found.")
    if atlas_name not in text_assets:
        raise RuntimeError(f"TextAsset '{atlas_name}' was not found.")

    atlas_bytes = text_assets[atlas_name]
    atlas_pages = get_atlas_pages(atlas_bytes)
    resolved_textures: list[tuple[str, object]] = []
    for page_name in atlas_pages:
        texture_name = Path(page_name).stem.lower()
        texture = textures.get(texture_name)
        if texture is None:
            raise RuntimeError(
                f"Texture2D '{Path(page_name).stem}' was not found."
            )
        resolved_textures.append((page_name, texture))

    output_directory.mkdir(parents=True, exist_ok=True)
    (output_directory / skeleton_name).write_bytes(text_assets[skeleton_name])
    (output_directory / atlas_name).write_bytes(atlas_bytes)

    for page_name, texture in resolved_textures:
        image = texture.image
        validate_image(image, f"Texture2D '{texture.m_Name}'")
        image.save(output_directory / page_name, format="PNG")

    print(
        f"Extracted {resource_id} {resource_type}: "
        f"{skeleton_name}, {atlas_name}, {', '.join(atlas_pages)}"
    )


def main() -> None:
    args = parse_args()
    resource_id = args.resource_id.lower()
    if not RESOURCE_ID_PATTERN.fullmatch(resource_id):
        raise RuntimeError(f"Invalid resource ID: {args.resource_id}")

    environment = UnityPy.load(str(args.bundle))
    if args.resource_type == "icons":
        extract_icon(environment, resource_id, args.output_directory)
        return

    extract_renderable(
        environment,
        resource_id,
        args.resource_type,
        args.output_directory,
    )


if __name__ == "__main__":
    main()

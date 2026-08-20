from __future__ import annotations

import argparse
from pathlib import Path

import UnityPy


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract one NIKKE character icon sprite from a decrypted bundle."
    )
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--resource-id", required=True)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    expected_name = f"mi_{args.resource_id.lower()}_s"
    environment = UnityPy.load(str(args.bundle))

    for obj in environment.objects:
        if obj.type.name != "Sprite":
            continue

        sprite = obj.read()
        if sprite.m_Name.lower() != expected_name:
            continue

        image = sprite.image
        if image.width <= 0 or image.height <= 0:
            raise RuntimeError(f"Sprite '{sprite.m_Name}' has invalid dimensions.")

        args.output.parent.mkdir(parents=True, exist_ok=True)
        image.save(args.output, format="PNG")
        print(
            f"Extracted {sprite.m_Name} "
            f"({image.width}x{image.height}) -> {args.output}"
        )
        return

    raise RuntimeError(
        f"Sprite '{expected_name}' was not found in '{args.bundle}'."
    )


if __name__ == "__main__":
    main()

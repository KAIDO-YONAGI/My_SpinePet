# SpinePet brand mark

The approved mark is a friendly pearl-white ghost with a curled forelock and
three rounded tail segments. It uses SpinePet's midnight navy, lavender,
indigo, cyan, and pearl-white interface palette.

## Files

- `spinepet-avatar.png` is the 1024 pixel transparent master used by the panel
  and project documentation.
- `spinepet-avatar-512.png` and `spinepet-avatar-256.png` are publishing
  exports.
- `SpinePet.ico` contains 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixel
  32-bit icon frames for Windows.
- `brand-assets.json` records the export dimensions, safe area, palette, and
  source hash.

## Provenance

The final raster artwork was produced with the built-in Codex ImageGen route
on 2026-08-13, refining the white-ghost direction selected by the project
owner. The final prompt preserved the friendly white ghost, curled forelock,
simple eyes and smile, and exactly three vertebra-like tail segments while
removing stars, blush, reflections, extra ornaments, and complex shading. It
was constrained to a centered, circular-crop-safe, small-size-readable mark in
the existing SpinePet palette.

Run `tools/brand-assets/Generate-BrandAssets.ps1` with the approved square
source artwork to regenerate the deterministic PNG and ICO files.

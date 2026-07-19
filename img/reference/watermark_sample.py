"""Watermark all images in this folder with diagonal red 'sample' text."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"}
TEXT = "sample"


def load_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        "C:/Windows/Fonts/arialbd.ttf",
        "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/segoeui.ttf",
    ]
    for path in candidates:
        try:
            return ImageFont.truetype(path, size=size)
        except OSError:
            continue
    return ImageFont.load_default()


def make_watermark(size: tuple[int, int], opacity: int) -> Image.Image:
    width, height = size
    diagonal = int((width**2 + height**2) ** 0.5)
    font_size = max(36, diagonal // 8)
    font = load_font(font_size)

    # Oversized canvas so rotated text still covers the full image
    canvas = Image.new("RGBA", (diagonal * 2, diagonal * 2), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    bbox = draw.textbbox((0, 0), TEXT, font=font)
    text_w = bbox[2] - bbox[0]
    text_h = bbox[3] - bbox[1]
    x = (canvas.width - text_w) // 2
    y = (canvas.height - text_h) // 2

    # Soft shadow so red text stays readable on light and dark sprites
    draw.text((x + 2, y + 2), TEXT, font=font, fill=(0, 0, 0, max(40, opacity // 3)))
    draw.text((x, y), TEXT, font=font, fill=(220, 30, 30, opacity))

    rotated = canvas.rotate(35, resample=Image.Resampling.BICUBIC, expand=False)
    left = (rotated.width - width) // 2
    top = (rotated.height - height) // 2
    return rotated.crop((left, top, left + width, top + height))


def watermark_image(path: Path, opacity: int) -> None:
    with Image.open(path) as img:
        rgba = img.convert("RGBA")
        mark = make_watermark(rgba.size, opacity)
        out = Image.alpha_composite(rgba, mark)

        suffix = path.suffix.lower()
        if suffix in {".jpg", ".jpeg"}:
            out.convert("RGB").save(path, quality=95)
        else:
            # Preserve PNG/WebP alpha; drop to original mode when it had no alpha
            if img.mode in {"RGB", "L", "P"} and suffix != ".png":
                out.convert(img.mode).save(path)
            else:
                out.save(path)


def main() -> None:
    parser = argparse.ArgumentParser(description="Diagonal red 'sample' watermark for reference images.")
    parser.add_argument(
        "--dir",
        type=Path,
        default=Path(__file__).resolve().parent,
        help="Folder of images to watermark (default: this script's folder)",
    )
    parser.add_argument(
        "--opacity",
        type=int,
        default=200,
        help="Text opacity 0-255 (default: 140)",
    )
    args = parser.parse_args()

    folder: Path = args.dir
    images = sorted(
        p for p in folder.iterdir() if p.is_file() and p.suffix.lower() in IMAGE_EXTENSIONS and p.name != Path(__file__).name
    )
    if not images:
        print(f"No images found in {folder}")
        return

    print(f"Watermarking {len(images)} image(s) in {folder} ...")
    for path in images:
        watermark_image(path, max(0, min(255, args.opacity)))
        print(f"  {path.name}")
    print("Done.")


if __name__ == "__main__":
    main()

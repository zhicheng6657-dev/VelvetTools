"""Generate every packaged Velvet Tools logo derivative from one original master.

The master is an original AI-assisted V mark recorded in
Assets/Brand/README.md.  This script only performs deterministic resizing,
recoloring and plate composition; it never downloads or copies third-party
artwork.
"""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "VelvetTools" / "Assets"
BRAND = ASSETS / "Brand"
MASTER_PATH = BRAND / "VelvetTools-V-master.png"

DARK_PLATE = (31, 39, 58, 255)
LIGHT_PLATE = (248, 250, 252, 255)


def fitted_mark(size: int, *, white: bool = False, coverage: float = 0.78) -> Image.Image:
    master = Image.open(MASTER_PATH).convert("RGBA")
    bbox = master.getchannel("A").getbbox()
    if bbox is None:
        raise RuntimeError(f"{MASTER_PATH} has no visible pixels")

    mark = master.crop(bbox)
    max_side = max(mark.width, mark.height)
    scale = (size * coverage) / max_side
    target = (
        max(1, round(mark.width * scale)),
        max(1, round(mark.height * scale)),
    )
    mark = mark.resize(target, Image.Resampling.LANCZOS)

    if white:
        alpha = mark.getchannel("A")
        mark = Image.new("RGBA", mark.size, (255, 255, 255, 0))
        mark.putalpha(alpha)

    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    x = (size - mark.width) // 2
    y = (size - mark.height) // 2
    canvas.alpha_composite(mark, (x, y))
    return canvas


def plate(size: int, color: tuple[int, int, int, int]) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    radius = round(size * 0.22)
    draw.rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=color)
    image.alpha_composite(fitted_mark(size))
    return image


def main() -> None:
    if not MASTER_PATH.exists():
        raise FileNotFoundError(MASTER_PATH)

    BRAND.mkdir(parents=True, exist_ok=True)

    fitted_mark(512).save(BRAND / "VelvetTools-V-color.png", optimize=True)
    fitted_mark(512, white=True).save(BRAND / "VelvetTools-V-white.png", optimize=True)

    plate(256, DARK_PLATE).save(ASSETS / "logo.png", optimize=True)
    plate(256, LIGHT_PLATE).save(ASSETS / "logo-light.png", optimize=True)

    for size in (16, 24, 32):
        plate(size, DARK_PLATE).save(ASSETS / f"tray-dark-{size}.png", optimize=True)
        plate(size, LIGHT_PLATE).save(ASSETS / f"tray-light-{size}.png", optimize=True)

    app_icon = plate(1024, DARK_PLATE)
    app_icon.save(
        ASSETS / "app.ico",
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40),
               (48, 48), (64, 64), (128, 128), (256, 256)],
    )


if __name__ == "__main__":
    main()

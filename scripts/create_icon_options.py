"""Create original vector icon options and a tray-size comparison preview."""

from pathlib import Path
import argparse

from PIL import Image, ImageDraw, ImageFont


PROJECT = Path(__file__).resolve().parents[1]
WORKSPACE = PROJECT.parent
FONT = Path("C:/Windows/Fonts/YuGothB.ttc")


def draw_icon(key, size=256):
    scale = size / 64
    image = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(image)
    colors = {"a": "#126D9B", "b": "#C76721", "c": "#087E73", "d": "#6553BC"}
    svg = [f'<rect x="2" y="2" width="60" height="60" rx="15" fill="{colors[key]}"/>']
    draw.rounded_rectangle(
        (2 * scale, 2 * scale, 62 * scale, 62 * scale), radius=15 * scale, fill=colors[key]
    )

    def rectangle(x, y, w, h, fill="white", radius=0):
        draw.rounded_rectangle(
            (x * scale, y * scale, (x + w) * scale, (y + h) * scale),
            radius=radius * scale,
            fill=fill,
        )
        svg.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{radius}" fill="{fill}"/>')

    def line(points, width=4, fill="white"):
        draw.line(
            [(x * scale, y * scale) for x, y in points],
            fill=fill,
            width=round(width * scale),
            joint="curve",
        )
        svg.append(
            f'<polyline points="{" ".join(f"{x},{y}" for x, y in points)}" fill="none" stroke="{fill}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>'
        )

    if key == "a":
        rectangle(11, 31, 42, 23, radius=5)
        for y in (36, 42):
            for x in (17, 25, 33, 41):
                rectangle(x, y, 5, 3, colors[key], 1)
        rectangle(22, 48, 20, 3, colors[key], 1)
        line([(18, 22), (24, 17), (32, 15), (40, 17), (46, 22)], 4)
        line([(26, 26), (32, 23), (38, 26)], 4)
    elif key == "b":
        font = ImageFont.truetype(str(FONT), round(39 * scale))
        draw.text((10 * scale, 5 * scale), "あ", font=font, fill="white", stroke_width=0)
        svg.append(
            '<text x="10" y="43" font-family="Yu Gothic" font-size="39" font-weight="bold" fill="white">あ</text>'
        )
        line([(23, 51), (48, 51), (42, 45)], 4, "#FFE3A6")
        line([(48, 51), (42, 57)], 4, "#FFE3A6")
    elif key == "c":
        rectangle(10, 15, 23, 19, radius=3)
        rectangle(14, 19, 15, 11, colors[key], 1)
        rectangle(31, 32, 23, 19, radius=3)
        rectangle(35, 36, 15, 11, colors[key], 1)
        line([(18, 40), (18, 46), (26, 46)], 4, "#B2F5DC")
        line([(39, 21), (46, 21), (46, 27)], 4, "#B2F5DC")
    else:
        rectangle(13, 13, 28, 37, radius=4)
        for y, width in ((21, 16), (29, 16), (37, 9)):
            rectangle(19, y, width, 3, colors[key], 1)
        rectangle(34, 35, 20, 20, colors[key], 4)
        line([(34, 45), (53, 45), (45, 37)], 5, "#DAD3FF")
        line([(53, 45), (45, 53)], 5, "#DAD3FF")
    return image, '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">' + "".join(
        svg
    ) + "</svg>\n"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--preview", required=True)
    args = parser.parse_args()
    output = (WORKSPACE / args.preview).resolve()
    if not output.is_relative_to(WORKSPACE):
        raise ValueError("Output outside workspace")
    assets = PROJECT / "assets/icon-options"
    assets.mkdir(parents=True, exist_ok=True)
    board = Image.new("RGB", (1120, 510), "#F1F5F9")
    draw = ImageDraw.Draw(board)
    title = ImageFont.truetype(str(FONT), 25)
    label = ImageFont.truetype(str(FONT), 18)
    small = ImageFont.truetype(str(FONT), 14)
    draw.text((30, 18), "JIS BLE Input  アイコン候補", font=title, fill="#172533")
    names = {
        "a": "キーボード＋無線",
        "b": "日本語＋送信",
        "c": "2台のPCをつなぐ",
        "d": "文章を送る",
    }
    for index, key in enumerate(names):
        icon, svg = draw_icon(key)
        (assets / f"{key}.svg").write_text(svg, encoding="utf-8")
        icon.save(assets / f"{key}.png")
        icon.save(
            assets / f"{key}.ico",
            sizes=[
                (16, 16),
                (20, 20),
                (24, 24),
                (32, 32),
                (48, 48),
                (64, 64),
                (128, 128),
                (256, 256),
            ],
        )
        x = 28 + index * 276
        draw.rounded_rectangle((x, 68, x + 260, 475), 16, fill="white")
        board.paste(
            icon.resize((96, 96), Image.Resampling.LANCZOS),
            (x + 82, 94),
            icon.resize((96, 96), Image.Resampling.LANCZOS),
        )
        draw.text((x + 20, 213), f"{key.upper()}  {names[key]}", font=label, fill="#172533")
        draw.text((x + 20, 260), "実寸  16 / 24 / 32 / 48px", font=small, fill="#64748B")
        for row, background in enumerate(("#F4F4F5", "#252832")):
            y = 300 + row * 76
            draw.rounded_rectangle((x + 16, y, x + 244, y + 60), 10, fill=background)
            for size, offset in ((16, 20), (24, 61), (32, 110), (48, 167)):
                reduced = icon.resize((size, size), Image.Resampling.LANCZOS)
                board.paste(reduced, (x + offset, y + (60 - size) // 2), reduced)
    output.parent.mkdir(parents=True, exist_ok=True)
    board.save(output)
    print(output)


if __name__ == "__main__":
    main()

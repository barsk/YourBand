"""Generate a printable sheet of ArUco markers to tape onto the guitar.

The markers are optional for the first hand-tracking test, but footage that
contains them can later be used to locate the guitar neck exactly, so hands
can be expressed relative to the fretboard without re-filming.

Print the PDF at 100 % / "actual size" and check the 50 mm ruler.

Usage:
  python capture/make_markers.py
"""
from pathlib import Path

import cv2
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ARUCO_DICT = cv2.aruco.DICT_4X4_50
# id -> printed side length in mm, and where it goes on the guitar.
MARKER_SIZES_MM = {0: 25, 1: 40}
MARKER_PLACES = {0: "huvudet (headstock), framsidan", 1: "kroppen, nära halsfästet"}

DPI = 300
A4_MM = (210, 297)


def mm(v):
    return round(v / 25.4 * DPI)


def font(size_mm):
    try:
        return ImageFont.truetype("arial.ttf", mm(size_mm))
    except OSError:
        return ImageFont.load_default()


def main():
    dictionary = cv2.aruco.getPredefinedDictionary(ARUCO_DICT)
    page = Image.new("L", (mm(A4_MM[0]), mm(A4_MM[1])), 255)
    draw = ImageDraw.Draw(page)
    draw.text((mm(20), mm(15)), "GuitarTutor – ArUco-markörer (DICT_4X4_50)", font=font(6), fill=0)
    draw.text((mm(20), mm(25)), "Skriv ut i 100 % / verklig storlek. Linjen nedan ska vara exakt 50 mm.",
              font=font(4), fill=0)
    draw.line((mm(20), mm(33), mm(70), mm(33)), fill=0, width=mm(0.5))
    for x in (20, 70):
        draw.line((mm(x), mm(31), mm(x), mm(35)), fill=0, width=mm(0.5))

    y = 50
    for marker_id, size in MARKER_SIZES_MM.items():
        img = cv2.aruco.generateImageMarker(dictionary, marker_id, mm(size))
        page.paste(Image.fromarray(img), (mm(20), mm(y)))
        draw.text((mm(30 + size), mm(y)), f"id {marker_id}  –  {size} mm", font=font(6), fill=0)
        draw.text((mm(30 + size), mm(y + 9)), f"Placering: {MARKER_PLACES[marker_id]}", font=font(4), fill=0)
        draw.text((mm(30 + size), mm(y + 16)), "Klipp med minst 4 mm vit kant runt om.", font=font(4), fill=0)
        y += size + 25

    out_dir = ROOT / "markers"
    out_dir.mkdir(exist_ok=True)
    page.save(out_dir / "aruco_markers.png", dpi=(DPI, DPI))
    page.convert("RGB").save(out_dir / "aruco_markers.pdf", resolution=DPI)
    print(f"-> {out_dir / 'aruco_markers.pdf'}")


if __name__ == "__main__":
    main()

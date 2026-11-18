"""Draws the pet's name tag (like VRChat's own): a dark rounded bar, a round icon with a green dot, the name,
a pronoun pill and a status line. Run from the project root: python Tools/translator/nametag.py
Reads Assets/natsuki_veer02/Translator/NameTag_Icon.png (TranslatorIconCapture), writes NameTag.png next to it."""
from PIL import Image, ImageDraw, ImageFont

DIR = "Assets/natsuki_veer02/Translator/"
NAME, PRONOUN, STATUS = "Homare2026", "AI", "Xin Chao"
FONT = "C:/Windows/Fonts/segoeui.ttf"
S = 2  # drawn at twice the size, then shrunk: smooth edges
W, H = 768, 256
BAR = (16, 26, 46, 230)


def font(size):
    return ImageFont.truetype(FONT, size * S)


img = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((8 * S, 28 * S, (W - 8) * S, (H - 28) * S), radius=(H - 56) // 2 * S, fill=BAR)

# Icon: a circle, cut from the photo.
r = 88
cx, cy = 28 + r, H // 2
icon = Image.open(DIR + "NameTag_Icon.png").convert("RGBA").resize((2 * r * S, 2 * r * S), Image.LANCZOS)
mask = Image.new("L", icon.size, 0)
ImageDraw.Draw(mask).ellipse((0, 0, icon.size[0] - 1, icon.size[1] - 1), fill=255)
img.paste(icon, ((cx - r) * S, (cy - r) * S), mask)
# Online dot, bottom right of the icon, ringed with the bar colour.
dx, dy, dr = cx + int(r * 0.66), cy + int(r * 0.66), 20
d.ellipse(((dx - dr - 5) * S, (dy - dr - 5) * S, (dx + dr + 5) * S, (dy + dr + 5) * S), fill=BAR)
d.ellipse(((dx - dr) * S, (dy - dr) * S, (dx + dr) * S, (dy + dr) * S), fill=(40, 190, 70, 255))

x = cx + r + 34
d.text((x * S, 46 * S), NAME, font=font(78), fill=(235, 238, 245, 255))
# Pronoun pill, then the status.
pf, sf = font(36), font(38)
pw = d.textlength(PRONOUN, font=pf) / S
d.rounded_rectangle((x * S, 152 * S, (x + pw + 36) * S, 198 * S), radius=23 * S, fill=(38, 50, 74, 255))
d.text(((x + 18) * S, 150 * S), PRONOUN, font=pf, fill=(225, 228, 235, 255))
d.text(((x + pw + 56) * S, 148 * S), STATUS, font=sf, fill=(200, 205, 215, 255))

img.resize((W, H), Image.LANCZOS).save(DIR + "NameTag.png")
print("wrote", DIR + "NameTag.png")

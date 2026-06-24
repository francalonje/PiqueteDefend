"""Genera el set de iconos de slot (blanco sobre transparente) para PiqueteDefend.

No es parte del build: produce PNGs en Presentation/Resources/Icons/ que la UI carga
en runtime (Resources.Load<Texture2D>("Icons/<key>")) y usa como background del chip.
Render a 4x + downscale LANCZOS para bordes nítidos. Reejecutar si se cambian los diseños.

Uso:  py tools/gen_icons.py
"""
import math
import os
from PIL import Image, ImageDraw, ImageFont

S = 4
SZ = 64
N = SZ * S            # 256
W = (255, 255, 255, 255)
CLEAR = (0, 0, 0, 0)
C = N // 2            # 128

DST = os.path.join(os.path.dirname(__file__), "..", "PiqueteDefend", "Assets",
                   "PiqueteDefend", "Presentation", "Resources", "Icons")
DST = os.path.normpath(DST)


def canvas():
    img = Image.new("RGBA", (N, N), CLEAR)
    return img, ImageDraw.Draw(img)


def save(img, name):
    img.resize((SZ, SZ), Image.LANCZOS).save(os.path.join(DST, name + ".png"))


def ngon(cx, cy, n, r, rot=-90):
    return [(cx + r * math.cos(math.radians(rot + i * 360.0 / n)),
             cy + r * math.sin(math.radians(rot + i * 360.0 / n))) for i in range(n)]


def star(cx, cy, points, r_out, r_in, rot=-90):
    pts = []
    for i in range(points * 2):
        ang = math.radians(rot + i * 180.0 / points)
        r = r_out if i % 2 == 0 else r_in
        pts.append((cx + r * math.cos(ang), cy + r * math.sin(ang)))
    return pts


def gear(cx, cy, teeth, r_out, r_in):
    pts, steps = [], teeth * 4
    for i in range(steps):
        ang = math.radians(i * 360.0 / steps)
        r = r_out if (i % 4) in (1, 2) else r_in
        pts.append((cx + r * math.cos(ang), cy + r * math.sin(ang)))
    return pts


def font(size):
    for p in (r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\arial.ttf"):
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


# ── Iconos ────────────────────────────────────────────────────────────────
def i_atk(d):                                    # espada vertical
    d.polygon([(128, 26), (146, 74), (146, 150), (110, 150), (110, 74)], fill=W)
    d.rectangle([92, 150, 164, 166], fill=W)     # guarda
    d.rectangle([118, 166, 138, 206], fill=W)    # empuñadura
    d.ellipse([114, 200, 142, 226], fill=W)      # pomo


def i_heal(d):                                   # cruz
    d.rounded_rectangle([108, 50, 148, 206], radius=10, fill=W)
    d.rounded_rectangle([50, 108, 206, 148], radius=10, fill=W)


def i_res_dinero(d, img):                        # moneda con $
    d.ellipse([38, 38, 218, 218], fill=W)
    d.ellipse([60, 60, 196, 196], fill=CLEAR)
    f = font(120)
    d.text((C, C), "$", font=f, fill=W, anchor="mm")


def i_res_fuerza(d):                             # rayo
    d.polygon([(150, 26), (90, 140), (128, 140), (104, 230),
               (178, 102), (134, 102)], fill=W)


def i_res_social(d):                             # globo de diálogo
    d.rounded_rectangle([44, 50, 212, 166], radius=30, fill=W)
    d.polygon([(82, 162), (82, 214), (126, 162)], fill=W)


def i_regen(d):                                  # corazón
    d.ellipse([56, 70, 134, 148], fill=W)
    d.ellipse([122, 70, 200, 148], fill=W)
    d.polygon([(58, 116), (198, 116), (128, 214)], fill=W)


def i_aura(d):                                   # destello 4 puntas
    d.polygon(star(C, C, 4, 98, 34), fill=W)


def i_thorns(d):                                 # escudo
    d.polygon([(56, 44), (200, 44), (200, 116), (128, 222), (56, 116)], fill=W)


def i_turndmg(d):                                # llama / gota hacia arriba
    d.ellipse([72, 104, 184, 216], fill=W)
    d.polygon([(128, 32), (74, 150), (182, 150)], fill=W)


def i_turnstatus(d):                             # hexágono (estado/peligro)
    d.polygon(ngon(C, C, 6, 94), fill=W)
    d.polygon(ngon(C, C, 6, 56), fill=CLEAR)


def i_poison(d):                                 # calavera
    d.ellipse([54, 46, 202, 186], fill=W)        # cráneo
    d.rounded_rectangle([86, 168, 170, 214], radius=10, fill=W)   # mandíbula
    d.ellipse([84, 94, 126, 138], fill=CLEAR)    # ojo izq
    d.ellipse([130, 94, 172, 138], fill=CLEAR)   # ojo der
    d.polygon([(128, 140), (116, 162), (140, 162)], fill=CLEAR)   # nariz
    d.rectangle([108, 182, 118, 214], fill=CLEAR)  # diente
    d.rectangle([138, 182, 148, 214], fill=CLEAR)


def i_stun(d):                                   # estrella de impacto
    d.polygon(star(C, C, 12, 100, 50), fill=W)


def i_furia(d):                                  # flecha arriba
    d.polygon([(128, 32), (64, 120), (192, 120)], fill=W)
    d.rectangle([108, 120, 148, 212], fill=W)


def i_desmor(d):                                 # flecha abajo
    d.polygon([(128, 224), (64, 136), (192, 136)], fill=W)
    d.rectangle([108, 44, 148, 136], fill=W)


def i_equip(d):                                  # engranaje
    d.polygon(gear(C, C, 8, 102, 80), fill=W)
    d.ellipse([94, 94, 162, 162], fill=CLEAR)


def i_passive(d):                                # punto genérico
    d.ellipse([86, 86, 170, 170], fill=W)


def i_atk_aoe(d):                                # estallido (ataque a todas / AoE)
    d.polygon(star(C, C, 8, 108, 36), fill=W)


def i_heal_aoe(d):                               # cruz en anillo (cura en área / a todas)
    d.ellipse([42, 42, 214, 214], fill=W)
    d.ellipse([66, 66, 190, 190], fill=CLEAR)
    d.rounded_rectangle([116, 82, 140, 174], radius=6, fill=W)
    d.rounded_rectangle([82, 116, 174, 140], radius=6, fill=W)


SIMPLE = {
    "atk": i_atk, "atk-aoe": i_atk_aoe, "heal": i_heal, "heal-aoe": i_heal_aoe,
    "res-fuerza": i_res_fuerza, "res-social": i_res_social, "regen": i_regen,
    "aura": i_aura, "thorns": i_thorns, "turndmg": i_turndmg, "turnstatus": i_turnstatus,
    "poison": i_poison, "stun": i_stun, "furia": i_furia, "desmor": i_desmor,
    "equip": i_equip, "passive": i_passive,
}


def main():
    os.makedirs(DST, exist_ok=True)
    for name, fn in SIMPLE.items():
        img, d = canvas()
        fn(d)
        save(img, name)
    img, d = canvas()                            # dinero necesita img para el texto
    i_res_dinero(d, img)
    save(img, "res-dinero")
    print(f"OK: {len(SIMPLE) + 1} iconos en {DST}")


if __name__ == "__main__":
    main()

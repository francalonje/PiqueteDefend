# Arte de unidades (tablero)

Texturas que `GameController.ApplyUnitArt(...)` carga por convención vía `Resources.Load<Texture2D>("Units/<key>")`.

Prioridad de resolución (dev-guide §5.1):

1. `CardData.sprite` asignado en el asset de la carta — si existe, manda.
2. `Units/{id}.png` — **sprite propio de esa unidad**. El `{id}` es el de la carta
   (`CardData.id`, ej. `piquetero`). Hoy las 9 unidades de cada facción tienen el suyo.
3. `Units/{faccion}-default.png` — **fallback de facción** (red de seguridad para unidades
   nuevas sin arte propio). `{faccion}` es el nombre del enum `Faction` en minúscula:
   `policias-default.png` / `manifestantes-default.png`.

> Arte placeholder vieja y las hojas-fuente (`*-todos-v2.png`) quedan archivadas en `old/`.

Para darle sprite propio a una unidad: dejá un PNG llamado como su `id` en esta carpeta.
No hace falta tocar código.

> Importá los PNG como **Texture2D** (default). El área de arte usa `background-size: cover`
> (`.slot__art` en `Game.uss`). Binarios versionados por Git LFS (`*.png lfs`).

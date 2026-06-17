Audio del juego (placeholders)
==============================

Dropeá los archivos de audio acá con estos nombres exactos (la extensión puede ser
.wav, .ogg o .mp3 — se cargan por nombre, sin importar la extensión):

  button-click.*   -> SFX al clickear botones (menú, facciones, Jugar/Descartar, overlay)
  card-click.*     -> SFX al seleccionar una carta de la mano
  music-lobby.*    -> Música del menú y selección de facción (loop)
  music-game.*     -> Música dentro de la partida (loop)

El AudioManager (Presentation/App/AudioManager.cs) los carga desde Resources/Audio/
por nombre. Si un archivo no existe, simplemente no suena (sin errores).

Para agregar más sonidos: sumá una constante en AudioId.cs y llamá
AudioManager.Instance.PlaySfx("nombre") / PlayMusic("nombre") donde corresponda.

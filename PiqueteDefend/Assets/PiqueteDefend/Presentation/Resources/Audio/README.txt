Audio del juego (placeholders)
==============================

Dropeá los archivos de audio acá con estos nombres exactos (la extensión puede ser
.wav, .ogg o .mp3 — se cargan por nombre, sin importar la extensión):

  button-click.*          -> SFX al clickear botones (menú, facciones, Jugar/Descartar, overlay)
  card-click.*            -> SFX al seleccionar una carta de la mano
  music-main.*            -> Música del menú de inicio (loop)
  music-faction-select.*  -> Música de la selección de jugador (loop)
  music-game.*            -> Música dentro de la partida (loop)

Hoy los tres music-* son el mismo audio (audiobg); son archivos separados a propósito
para poder ponerle una pista distinta a cada pantalla en el futuro sin tocar código.

El AudioManager (Presentation/App/AudioManager.cs) los carga desde Resources/Audio/
por nombre. Si un archivo no existe, simplemente no suena (sin errores).

Para agregar más sonidos: sumá una constante en AudioId.cs y llamá
AudioManager.Instance.PlaySfx("nombre") / PlayMusic("nombre") donde corresponda.

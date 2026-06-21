namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Nombres de los clips de audio. Cada uno se carga desde <c>Resources/Audio/&lt;nombre&gt;</c>
    /// (cualquier extensión: .wav/.ogg/.mp3). Hoy son placeholders: si el archivo no existe,
    /// el AudioManager simplemente no reproduce nada. Agregar el audio = dropear el archivo
    /// con el nombre correspondiente en esa carpeta.
    /// </summary>
    public static class AudioId
    {
        // SFX
        public const string ButtonClick = "button-click";
        public const string CardClick = "card-click";

        // Música — un slot por pantalla. Hoy los tres apuntan al mismo audio (audiobg),
        // pero son archivos separados en Resources/Audio para poder divergir en el futuro
        // sin tocar código: basta reemplazar el .mp3 correspondiente.
        public const string MusicMain = "music-main";                   // menú de inicio
        public const string MusicFactionSelect = "music-faction-select"; // selección de jugador
        public const string MusicGame = "music-game";                   // partida
    }
}

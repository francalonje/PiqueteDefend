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

        // Música
        public const string MusicLobby = "music-lobby";
        public const string MusicGame = "music-game";
    }
}

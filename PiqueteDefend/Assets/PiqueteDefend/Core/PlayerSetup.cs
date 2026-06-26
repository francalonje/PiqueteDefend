using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Cómo arranca un jugador una partida (spec §7.8/§17.5). Es el punto de inyección que permite:
    /// <list type="bullet">
    /// <item><b>Deckbuilding</b> (spec §17.2): inyectar el <see cref="deck"/> de la run en vez de
    /// derivarlo de la facción.</item>
    /// <item><b>Dificultad por distancia</b> (spec §17.1): handicaps a la IA — recursos iniciales
    /// extra (<see cref="bonusDinero"/>/<see cref="bonusFuerza"/>/<see cref="bonusSocial"/>) y
    /// unidades iniciales extra (<see cref="extraStartingUnits"/>).</item>
    /// </list>
    /// Todos los campos son opcionales: con los defaults la partida es idéntica a la base 2-jugadores
    /// (por eso <see cref="GameEngine.StartGame(Faction,Faction,int)"/> sigue funcionando sin cambios).
    ///
    /// <para>[EXTENSIÓN] Cuando se implementen las reliquias (spec §17.4), sus bonos de setup
    /// (recursos/mano/unidades iniciales) se aplican poblando estos mismos campos para el jugador
    /// humano — no hace falta tocar el motor.</para>
    /// </summary>
    public sealed class PlayerSetup
    {
        public Faction faction;

        /// <summary>Mazo de robo a inyectar. <c>null</c> = el del catálogo para la facción
        /// (<see cref="ICardCatalog.GetDeckList"/>). Punto de inyección de deckbuilding (spec §7.8/§17.2).</summary>
        public IReadOnlyList<CardData> deck;

        /// <summary>Unidades iniciales base. <c>null</c> = las del catálogo
        /// (<see cref="ICardCatalog.GetStartingUnits"/>).</summary>
        public IReadOnlyList<UnitCardData> startingUnits;

        /// <summary>Unidades iniciales EXTRA, además de las base. Handicap por dificultad (spec §17.1).</summary>
        public IReadOnlyList<UnitCardData> extraStartingUnits;

        /// <summary>Recursos iniciales extra sobre el default de <see cref="GameConfig"/> (handicap).
        /// El total se recorta al <see cref="GameConfig.maxResource"/>.</summary>
        public int bonusDinero;
        public int bonusFuerza;
        public int bonusSocial;

        public PlayerSetup(Faction faction) => this.faction = faction;

        /// <summary>Setup base: facción pura, todo lo demás por default (idéntico a 2-jugadores).</summary>
        public static PlayerSetup ForFaction(Faction faction) => new PlayerSetup(faction);
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Arquetipo de enemigo curado (spec §17.6): la variedad de los combates de la run NO viene de
    /// subir un número, sino de enfrentar mazos/tableros/estilos distintos. Es <b>data</b>
    /// (ScriptableObject), consumida por <see cref="RunManager.BeginCombat"/> para armar el
    /// <see cref="PlayerSetup"/> de la IA — el motor no aprende nada nuevo (mismo seam que el handicap).
    ///
    /// <para>Un nodo de combate sortea un arquetipo de un pool sin repetir dentro de la run
    /// (<see cref="RunState.usedEncounterIds"/>). El jefe usa un arquetipo con
    /// <see cref="isBoss"/> = true: unidad-líder única + pasivas/estados propios (spec §17.6).</para>
    /// </summary>
    [CreateAssetMenu(fileName = "Encounter", menuName = "PiqueteDefend/Encounter")]
    public sealed class EncounterDefinition : ScriptableObject
    {
        /// <summary>Id estable (para no repetir en la run y para tests/presentación).</summary>
        public string id;

        /// <summary>Nombre temático del enemigo (flavor argento). Sólo presentación.</summary>
        public string title;

        /// <summary>Facción que controla la IA (normalmente la opuesta al humano).</summary>
        public Faction faction;

        /// <summary>Tier de dificultad al que apunta el arquetipo (= distancia del nodo, spec §17.1).
        /// El pool prefiere encuentros de este tier; si no hay, cae a cualquiera del tipo.</summary>
        public int difficulty;

        /// <summary>Mazo del enemigo: lista literal de cartas (multiset). El motor la copia y baraja.
        /// Vacía/null = el mazo default de la facción (<see cref="ICardCatalog.GetDeckList"/>).</summary>
        public List<CardData> deck = new List<CardData>();

        /// <summary>Unidades iniciales del enemigo. Vacía/null = las del catálogo
        /// (<see cref="ICardCatalog.GetStartingUnits"/>).</summary>
        public List<UnitCardData> startingUnits = new List<UnitCardData>();

        /// <summary>Recursos iniciales extra propios del arquetipo, además del handicap por distancia.</summary>
        public int bonusDinero;
        public int bonusFuerza;
        public int bonusSocial;

        /// <summary>Estados sembrados en la IA al iniciar (ej. el jefe arranca con doble producción).</summary>
        public List<StatusEffect> aiInitialStatuses = new List<StatusEffect>();

        /// <summary>Estados sembrados en el HUMANO al iniciar (pasiva de jefe que castiga al jugador,
        /// ej. "tus unidades arrancan desmoralizadas"). Vacío en combates normales.</summary>
        public List<StatusEffect> playerInitialStatuses = new List<StatusEffect>();

        // ── Sólo jefe (spec §17.6) ────────────────────────────────────────────────

        /// <summary>True si este arquetipo es el jefe (cabecera de línea). Ganarlo gana el acto.</summary>
        public bool isBoss;

        /// <summary>Unidad-líder única del jefe: se despliega como unidad inicial EXTRA. Sus pasivas
        /// son la "pasiva de combate propia" del jefe (spec §17.6), sin tocar el motor.</summary>
        public UnitCardData leaderUnit;
    }
}

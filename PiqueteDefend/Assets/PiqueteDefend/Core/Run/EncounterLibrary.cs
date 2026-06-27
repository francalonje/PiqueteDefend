using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Fuente de verdad de los arquetipos de enemigo del acto 1 (spec §17.6), en código (espejo del
    /// patrón de <see cref="CardLibrary"/>). Los arma <b>en memoria</b> a partir del catálogo
    /// (referencia las MISMAS cartas persistidas, sin duplicar assets): la diferenciación viene del
    /// <b>tablero de apertura</b> (unidades iniciales por subtype) + handicap/estados, sobre el mazo
    /// default de la facción. Valores <b>rough</b>, a iterar por playtest ([[feedback-playtest-driven]]).
    ///
    /// <para>El pool trae arquetipos de <b>ambas</b> facciones; <see cref="RunManager"/> filtra por la
    /// facción de la IA (la opuesta al humano). Un arquetipo degenerado (a la facción le falta un subtype)
    /// cae a las unidades iniciales del catálogo, sin romper nada.</para>
    /// </summary>
    public static class EncounterLibrary
    {
        /// <summary>Pool completo del acto 1 (ambas facciones). Pasar a <see cref="RunManager"/>.</summary>
        public static List<EncounterDefinition> BuildActo1Pool(ICardCatalog catalog)
        {
            var pool = new List<EncounterDefinition>();
            pool.AddRange(BuildForFaction(catalog, Faction.Manifestantes));
            pool.AddRange(BuildForFaction(catalog, Faction.Policias));
            return pool;
        }

        private static List<EncounterDefinition> BuildForFaction(ICardCatalog catalog, Faction f)
        {
            List<UnitCardData> atacantes = UnitsOf(catalog, f, UnitSubtype.Atacante);
            List<UnitCardData> defensivas = UnitsOf(catalog, f, UnitSubtype.Defensiva);
            List<UnitCardData> productoras = UnitsOf(catalog, f, UnitSubtype.Productora);
            var deck = new List<CardData>(catalog.GetDeckList(f));

            bool manif = f == Faction.Manifestantes;
            var result = new List<EncounterDefinition>();

            // Rush: abre con atacantes — presiona el frente desde el turno 1.
            EncounterDefinition patota = Make(Id(f, "patota"), manif ? "La Columna" : "El Operativo",
                f, difficulty: 1, deck, Take(atacantes, 2));
            patota.bonusFuerza = 2;
            result.Add(patota);

            // Búnker: muros + una productora, gana por aguante; arranca con plata extra.
            var bunkerUnits = new List<UnitCardData>();
            bunkerUnits.AddRange(Take(defensivas, 2));
            bunkerUnits.AddRange(Take(productoras, 1));
            EncounterDefinition bunker = Make(Id(f, "bunker"), manif ? "El Acampe" : "El Vallado",
                f, difficulty: 2, deck, bunkerUnits);
            bunker.bonusDinero = 4;
            result.Add(bunker);

            // Aparato: economía — productoras + un cuerpo, arranca con doble producción.
            var aparatoUnits = new List<UnitCardData>();
            aparatoUnits.AddRange(Take(productoras, 2));
            aparatoUnits.AddRange(Take(defensivas, 1));
            EncounterDefinition aparato = Make(Id(f, "aparato"), manif ? "La Mesa Sindical" : "La Rosca Oficial",
                f, difficulty: 3, deck, aparatoUnits);
            aparato.bonusSocial = 3;
            aparato.aiInitialStatuses.Add(new StatusEffect(StatusType.DoubleProduction, 2, 1));
            result.Add(aparato);

            // Jefe (cabecera): starters default + unidad-líder (el atacante más resistente) + desmoraliza
            // al humano al arrancar (pasiva de jefe, sin tocar el motor).
            EncounterDefinition jefe = Make(Id(f, "jefe"), manif ? "La Conducción" : "La Cúpula",
                f, difficulty: 4, deck, new List<UnitCardData>(catalog.GetStartingUnits(f)), isBoss: true);
            jefe.leaderUnit = Strongest(atacantes);
            jefe.playerInitialStatuses.Add(new StatusEffect(StatusType.Desmoralizar, 2, 2));
            result.Add(jefe);

            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static EncounterDefinition Make(string id, string title, Faction f, int difficulty,
                                                List<CardData> deck, List<UnitCardData> units, bool isBoss = false)
        {
            var e = ScriptableObject.CreateInstance<EncounterDefinition>();
            e.id = id;
            e.title = title;
            e.faction = f;
            e.difficulty = difficulty;
            e.isBoss = isBoss;
            e.deck = new List<CardData>(deck);
            e.startingUnits = new List<UnitCardData>(units);
            e.aiInitialStatuses = new List<StatusEffect>();
            e.playerInitialStatuses = new List<StatusEffect>();
            return e;
        }

        private static string Id(Faction f, string slug) =>
            (f == Faction.Manifestantes ? "manif_" : "pol_") + slug;

        private static List<UnitCardData> UnitsOf(ICardCatalog catalog, Faction f, UnitSubtype sub)
        {
            var r = new List<UnitCardData>();
            foreach (CardData c in catalog.GetPool(f))
                if (c is UnitCardData u && u.unitSubtype == sub) r.Add(u);
            return r;
        }

        /// <summary>Primeras <paramref name="n"/> de la lista (o menos si no alcanza).</summary>
        private static List<UnitCardData> Take(List<UnitCardData> src, int n)
        {
            var r = new List<UnitCardData>();
            for (int i = 0; i < n && i < src.Count; i++) r.Add(src[i]);
            return r;
        }

        /// <summary>La unidad más resistente de la lista (mayor maxHp); null si está vacía.</summary>
        private static UnitCardData Strongest(List<UnitCardData> src)
        {
            UnitCardData best = null;
            foreach (UnitCardData u in src)
                if (best == null || u.maxHp > best.maxHp) best = u;
            return best;
        }
    }
}

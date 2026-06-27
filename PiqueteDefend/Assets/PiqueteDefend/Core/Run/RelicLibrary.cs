using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Fuente de verdad de las reliquias del acto 1 (spec §17.4), en código (espejo de
    /// <see cref="EncounterLibrary"/>). Se arman <b>en memoria</b>; las que dan una unidad extra
    /// referencian una unidad del catálogo de la facción del humano. <see cref="RunManager"/> las
    /// reparte (tesoro/élite) y <see cref="RunManager.ApplyRelics"/> las vuelca al combate. Valores
    /// <b>rough</b>, a iterar por playtest ([[feedback-playtest-driven]]).
    /// </summary>
    public static class RelicLibrary
    {
        /// <summary>Pool de reliquias para la facción del humano (la única activa en la run).</summary>
        public static List<RelicData> BuildPool(ICardCatalog catalog, Faction faction)
        {
            bool manif = faction == Faction.Manifestantes;
            var pool = new List<RelicData>
            {
                Resource("la_caja", manif ? "La Caja Chica" : "La Partida Reservada",
                    "Arrancás cada combate con $ Dinero extra.", ResourceType.Dinero, 3),
                Resource("el_aguante", "El Aguante",
                    "Más ⚡ Fuerza desde el arranque para salir a romperla.", ResourceType.Fuerza, 2),
                Resource("la_rosca", manif ? "La Viralización" : "La Rosca Mediática",
                    "Más 📣 para imponer agenda desde el principio.", ResourceType.Social, 3),

                Status("doble_turno", "Doble Turno",
                    "Tu primera producción del combate viene doble.",
                    new StatusEffect(StatusType.DoubleProduction, 2, 1)),
                Status("envion", "El Envión",
                    "Tus unidades iniciales arrancan envalentonadas (+daño).",
                    new StatusEffect(StatusType.Furia, 2, 2)),
            };

            // Reliquia de unidad extra (depende de la facción): un cuerpo inicial gratis.
            IReadOnlyList<UnitCardData> starters = catalog.GetStartingUnits(faction);
            if (starters != null && starters.Count > 0)
                pool.Add(Unit("el_refuerzo", manif ? "El Refuerzo" : "El Apoyo Logístico",
                    "Desplegás una unidad inicial extra de tu lado.", starters[0]));

            return pool;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static RelicData Make(string id, string name, string desc, RelicEffectKind kind)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            r.id = id;
            r.relicName = name;
            r.description = desc;
            r.kind = kind;
            return r;
        }

        private static RelicData Resource(string id, string name, string desc, ResourceType res, int value)
        {
            RelicData r = Make(id, name, desc, RelicEffectKind.BonusResource);
            r.resource = res;
            r.value = value;
            return r;
        }

        private static RelicData Status(string id, string name, string desc, StatusEffect status)
        {
            RelicData r = Make(id, name, desc, RelicEffectKind.InitialStatus);
            r.status = status;
            return r;
        }

        private static RelicData Unit(string id, string name, string desc, UnitCardData unit)
        {
            RelicData r = Make(id, name, desc, RelicEffectKind.ExtraStartingUnit);
            r.unit = unit;
            return r;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Fuente de verdad de las cartas, en código (valores del simulador + flavor del spec).
    /// La usan tanto los tests (instancias en memoria) como el generador de assets del editor
    /// (las persiste como ScriptableObjects). Cambiar una carta = cambiarla acá.
    ///
    /// El orden importa: el primer elemento de cada pool es el que devuelve un RNG que elige
    /// índice 0 (usado por los tests de reposición de mano).
    /// </summary>
    public static class CardLibrary
    {
        public static List<CardData> BuildManifestantes()
        {
            const Faction M = Faction.Manifestantes;
            return new List<CardData>
            {
                Unit("piquetero", "Piquetero", M, UnitSubtype.Atacante, d: 2, f: 2,
                     flavor: "Lleva el bombo, la bandera y las ganas de parar todo. El GPS del camionero lo odia."),
                Unit("jubilado", "Jubilado", M, UnitSubtype.Defensiva, d: 5, f: 1,
                     flavor: "83 años, bastón y primera fila. La policía tiene miedo de lo que van a decir en la tele."),
                Unit("olla_popular", "Olla Popular", M, UnitSubtype.Productora, d: 2, s: 1, prod: ResourceType.Dinero,
                     flavor: "Arroz, fideos, solidaridad y una receta que nadie sabe de dónde salió."),
                Unit("quilombero", "Quilombero", M, UnitSubtype.Productora, f: 4, s: 1, prod: ResourceType.Fuerza,
                     flavor: "No sabe bien por qué pelea pero lo hace con todo. Indispensable."),
                Unit("tuitero", "Tuitero Militante", M, UnitSubtype.Productora, d: 1, s: 1, prod: ResourceType.Social,
                     flavor: "2.300 seguidores. Siente que cambió la historia con cada hilo."),

                Action("colecta", "Colecta", M, ActionCategory.Boost, s: 3,
                       flavor: "Pasamos el sombrero. No el de la policía. El de los compañeros.",
                       effects: ModRes(TargetType.Self, ResourceType.Dinero, 6)),
                Action("fernet", "Fernet con Cola", M, ActionCategory.Boost, d: 1,
                       flavor: "Hidratación táctica. Técnicamente no es doping si lo toma todo el mundo.",
                       effects: ModRes(TargetType.Self, ResourceType.Fuerza, 3)),
                Action("viral_redes", "Viral en Redes", M, ActionCategory.Boost, d: 2,
                       flavor: "Un video de 14 segundos. Tres millones de reproducciones. El ministerio ya llamó.",
                       effects: ModRes(TargetType.Self, ResourceType.Social, 7)),

                Action("saqueo", "Saqueo", M, ActionCategory.Sabotaje, f: 1,
                       flavor: "No es saqueo. Es redistribución urgente de recursos.",
                       effects: ModRes(TargetType.Opponent, ResourceType.Dinero, -3)),
                Action("asamblea_6hs", "Asamblea de 6 Horas", M, ActionCategory.Sabotaje, d: 2,
                       flavor: "Todos hablan. Nadie escucha. El orden del día tiene 47 puntos.",
                       effects: ModRes(TargetType.Opponent, ResourceType.Fuerza, -7)),
                Action("fake_news", "Fake News", M, ActionCategory.Sabotaje, s: 3,
                       flavor: "Una historia bien contada a tiempo. La verdad puede esperar.",
                       effects: ModRes(TargetType.Opponent, ResourceType.Social, -5)),
                Action("romper_marcha", "Romper la Marcha", M, ActionCategory.Sabotaje, f: 1, s: 3,
                       flavor: "Alguien tira una piedra donde no era. La marcha pierde el hilo. Todos se miran.",
                       effects: RemoveUnit(TargetType.Opponent)),

                Action("paro_general", "Paro General", M, ActionCategory.Ataque, d: 2, f: 3,
                       flavor: "24 horas de nada. El país en pausa. El bondi no viene. Ni el delivery.",
                       effects: ModHP(TargetType.Opponent, -14)),
                Action("abrazo_colectivo", "Abrazo Colectivo", M, ActionCategory.Defensa, d: 4, s: 1,
                       flavor: "El abrazo que cura todo. Menos la deuda. Pero todo lo demás.",
                       effects: ModHP(TargetType.Self, 16)),

                Action("corte_ruta", "Corte de Ruta", M, ActionCategory.EfectoEspecial, f: 1, s: 2,
                       flavor: "Neumáticos quemados, humo negro y el GPS del camionero obsoleto.",
                       effects: ApplyStatus(TargetType.Opponent, Skip())),
                Action("asamblea_popular", "Asamblea Popular", M, ActionCategory.EfectoEspecial, s: 6,
                       flavor: "Se vota a mano alzada. Algo sale. Esta vez salió bien.",
                       effects: ApplyStatus(TargetType.Self, Double())),
            };
        }

        public static List<CardData> BuildPolicias()
        {
            const Faction P = Faction.Policias;
            return new List<CardData>
            {
                Unit("patrullero", "Patrullero", P, UnitSubtype.Atacante, d: 2, f: 4,
                     flavor: "Sirena, luces y un oficial que lleva 14 horas de turno. No preguntes cómo está."),
                Unit("comisaria", "Comisaría", P, UnitSubtype.Defensiva, d: 2, f: 1,
                     flavor: "El edificio más antiguo del barrio. Sobrevivió cuatro gobiernos y una inundación."),
                Unit("subsidio", "Subsidio", P, UnitSubtype.Productora, d: 4, s: 1, prod: ResourceType.Dinero,
                     flavor: "El Estado se financia a sí mismo. Sostenible, dicen."),
                Unit("gorra_barrio", "Gorra de Barrio", P, UnitSubtype.Productora, f: 1, s: 2, prod: ResourceType.Fuerza,
                     flavor: "Lo conoce todo el mundo. Nadie sabe exactamente qué hace. Siempre está."),
                Unit("conferencia", "Conferencia de Prensa", P, UnitSubtype.Productora, d: 3, s: 2, prod: ResourceType.Social,
                     flavor: "El ministro sonríe. Los periodistas anotan. Nadie pregunta nada difícil."),

                Action("partida", "Partida Presupuestaria", P, ActionCategory.Boost, s: 1,
                       flavor: "Existe en el papel. Se aprobó a las 3 AM. Nadie sabe bien para qué.",
                       effects: ModRes(TargetType.Self, ResourceType.Dinero, 7)),
                Action("licitacion", "Licitación Express", P, ActionCategory.Boost, d: 3,
                       flavor: "Una empresa, un sobre, 48 horas. El pliego lo hicieron el lunes.",
                       effects: ModRes(TargetType.Self, ResourceType.Fuerza, 8)),
                Action("cadena_nacional", "Cadena Nacional", P, ActionCategory.Boost, f: 2,
                       flavor: "Interrumpe la novela. El presidente habla 40 minutos. Nadie pidió que pare.",
                       effects: ModRes(TargetType.Self, ResourceType.Social, 4)),

                Action("embargo", "Embargo", P, ActionCategory.Sabotaje, f: 3,
                       flavor: "El juez firmó. La plata se fue. El afectado ya lo sospechaba.",
                       effects: ModRes(TargetType.Opponent, ResourceType.Dinero, -7)),
                Action("detencion", "Detención", P, ActionCategory.Sabotaje, d: 1,
                       flavor: "Demorado para averiguación de antecedentes. O por las dudas. Principalmente por las dudas.",
                       effects: ModRes(TargetType.Opponent, ResourceType.Fuerza, -3)),
                Action("censura", "Censura", P, ActionCategory.Sabotaje, s: 2,
                       flavor: "El artículo fue dado de baja. Por razones técnicas. Técnicas.",
                       effects: ModRes(TargetType.Opponent, ResourceType.Social, -5)),
                Action("infiltrado", "Infiltrado", P, ActionCategory.Sabotaje, d: 3, s: 1,
                       flavor: "Un tipo raro en la marcha. Nadie lo conoce pero todos lo sospechaban.",
                       effects: RemoveUnit(TargetType.Opponent)),

                Action("operativo", "Operativo Apretón", P, ActionCategory.Ataque, d: 4, f: 2,
                       flavor: "Cuatro camiones, veinte efectivos, un drone. Para un jubilado con un cartel.",
                       effects: ModHP(TargetType.Opponent, -18)),
                Action("balas_goma", "Balas de Goma", P, ActionCategory.Defensa, d: 2, s: 3,
                       flavor: "No matan, dicen. Técnicamente. El protocolo fue actualizado en 2019.",
                       effects: ModHP(TargetType.Self, 12)),

                Action("toque_queda", "Toque de Queda", P, ActionCategory.EfectoEspecial, d: 4, f: 1,
                       flavor: "A las 22hs, todos adentro. El que salga averigua.",
                       effects: ApplyStatus(TargetType.Opponent, Skip())),
                Action("decreto", "Decreto de Emergencia", P, ActionCategory.EfectoEspecial, d: 3,
                       flavor: "El Congreso estaba de feria. Había urgencia. Siempre hay urgencia.",
                       effects: ApplyStatus(TargetType.Self, Double())),
            };
        }

        // ── Builders ──────────────────────────────────────────────────────────

        private static CardData Unit(string id, string name, Faction faction, UnitSubtype sub,
                                     int d = 0, int f = 0, int s = 0,
                                     ResourceType prod = ResourceType.Dinero, string flavor = "")
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            card.name = id;
            card.id = id;
            card.cardName = name;
            card.faction = faction;
            card.cardType = CardType.Unidad;
            card.unitSubtype = sub;
            card.productionResource = prod;
            card.costDinero = d; card.costFuerza = f; card.costSocial = s;
            card.flavorText = flavor;
            return card;
        }

        private static CardData Action(string id, string name, Faction faction, ActionCategory cat,
                                       int d = 0, int f = 0, int s = 0, string flavor = "",
                                       params CardEffect[] effects)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            card.name = id;
            card.id = id;
            card.cardName = name;
            card.faction = faction;
            card.cardType = CardType.Accion;
            card.actionCategory = cat;
            card.costDinero = d; card.costFuerza = f; card.costSocial = s;
            card.flavorText = flavor;
            card.effects = new List<CardEffect>(effects);
            return card;
        }

        private static CardEffect ModHP(TargetType t, int v) => new CardEffect(CardEffectType.ModifyHP, t, value: v);
        private static CardEffect ModRes(TargetType t, ResourceType r, int v) => new CardEffect(CardEffectType.ModifyResource, t, r, v);
        private static CardEffect RemoveUnit(TargetType t, int v = -1) => new CardEffect(CardEffectType.RemoveUnit, t, value: v);
        private static CardEffect ApplyStatus(TargetType t, StatusEffect s) => new CardEffect(CardEffectType.ApplyStatus, t, status: s);

        private static StatusEffect Skip() => new StatusEffect(StatusType.SkipProduction, 0, 1);
        private static StatusEffect Double() => new StatusEffect(StatusType.DoubleProduction, 2, 1);
    }
}

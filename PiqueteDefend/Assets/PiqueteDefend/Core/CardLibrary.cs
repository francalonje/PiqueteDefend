using System;
using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Fuente de verdad de las cartas, en código (valores del spec + flavor). La usa el generador
    /// de assets del editor (las persiste como ScriptableObjects). Cambiar una carta = cambiarla acá.
    ///
    /// MVP: todas las unidades comparten la <b>baseline</b> (spec §9/§10): 20 HP, ataque
    /// "1 slot a elección · 5 daño" y despliegue en cualquier slot. Costos de recurso único.
    /// Se diferenciará/balanceará después.
    /// </summary>
    public static class CardLibrary
    {
        public const int BaselineHp = 20;
        public const int BaselineDamage = 5;

        public static List<CardData> BuildManifestantes()
        {
            const Faction M = Faction.Manifestantes;
            return new List<CardData>
            {
                Unit("piquetero", "Piquetero", M, UnitSubtype.Atacante, ResourceType.Fuerza, 4,
                     "Lleva el bombo, la bandera y las ganas de parar todo. El GPS del camionero lo odia."),
                Unit("jubilado", "Jubilado", M, UnitSubtype.Defensiva, ResourceType.Dinero, 5,
                     "83 años, bastón y primera fila. La policía tiene miedo de lo que van a decir en la tele."),
                Unit("olla_popular", "Olla Popular", M, UnitSubtype.Productora, ResourceType.Dinero, 3,
                     "Arroz, fideos, solidaridad y una receta que nadie sabe de dónde salió.",
                     Produce(ResourceType.Dinero, 1)),
                Unit("quilombero", "Quilombero", M, UnitSubtype.Productora, ResourceType.Fuerza, 5,
                     "No sabe bien por qué pelea pero lo hace con todo. Indispensable.",
                     Produce(ResourceType.Fuerza, 1)),
                Unit("tuitero", "Tuitero Militante", M, UnitSubtype.Productora, ResourceType.Social, 2,
                     "2.300 seguidores. Siente que cambió la historia con cada hilo.",
                     Produce(ResourceType.Social, 1)),

                Action("colecta", "Colecta", M, ActionCategory.Boost, ResourceType.Social, 3,
                       "Pasamos el sombrero. No el de la policía. El de los compañeros.",
                       ModRes(TargetType.Self, ResourceType.Dinero, 6)),
                Action("fernet", "Fernet con Cola", M, ActionCategory.Boost, ResourceType.Dinero, 1,
                       "Hidratación táctica. Técnicamente no es doping si lo toma todo el mundo.",
                       ModRes(TargetType.Self, ResourceType.Fuerza, 3)),
                Action("viral_redes", "Viral en Redes", M, ActionCategory.Boost, ResourceType.Dinero, 2,
                       "Un video de 14 segundos. Tres millones de reproducciones. El ministerio ya llamó.",
                       ModRes(TargetType.Self, ResourceType.Social, 7)),

                Action("saqueo", "Saqueo", M, ActionCategory.Sabotaje, ResourceType.Fuerza, 1,
                       "No es saqueo. Es redistribución urgente de recursos.",
                       ModRes(TargetType.Opponent, ResourceType.Dinero, -3)),
                Action("asamblea_6hs", "Asamblea de 6 Horas", M, ActionCategory.Sabotaje, ResourceType.Dinero, 2,
                       "Todos hablan. Nadie escucha. El orden del día tiene 47 puntos.",
                       ModRes(TargetType.Opponent, ResourceType.Fuerza, -7)),
                Action("fake_news", "Fake News", M, ActionCategory.Sabotaje, ResourceType.Social, 3,
                       "Una historia bien contada a tiempo. La verdad puede esperar.",
                       ModRes(TargetType.Opponent, ResourceType.Social, -5)),
                Action("romper_marcha", "Romper la Marcha", M, ActionCategory.Sabotaje, ResourceType.Social, 4,
                       "Alguien tira una piedra donde no era. La marcha pierde el hilo. Todos se miran.",
                       ModHP(TargetType.Opponent, -1)),

                Action("paro_general", "Paro General", M, ActionCategory.Ataque, ResourceType.Fuerza, 5,
                       "24 horas de nada. El país en pausa. El bondi no viene. Ni el delivery.",
                       ModHP(TargetType.Opponent, -14)),
                Action("abrazo_colectivo", "Abrazo Colectivo", M, ActionCategory.Defensa, ResourceType.Dinero, 5,
                       "El abrazo que cura todo. Menos la deuda. Pero todo lo demás.",
                       ModHP(TargetType.Self, 16)),

                Action("corte_ruta", "Corte de Ruta", M, ActionCategory.EfectoEspecial, ResourceType.Social, 3,
                       "Neumáticos quemados, humo negro y el GPS del camionero obsoleto.",
                       ApplyStatus(TargetType.Opponent, Skip())),
                Action("asamblea_popular", "Asamblea Popular", M, ActionCategory.EfectoEspecial, ResourceType.Social, 6,
                       "Se vota a mano alzada. Algo sale. Esta vez salió bien.",
                       ApplyStatus(TargetType.Self, Double())),
            };
        }

        public static List<CardData> BuildPolicias()
        {
            const Faction P = Faction.Policias;
            return new List<CardData>
            {
                Unit("patrullero", "Patrullero", P, UnitSubtype.Atacante, ResourceType.Fuerza, 6,
                     "Sirena, luces y un oficial que lleva 14 horas de turno. No preguntes cómo está."),
                Unit("comisaria", "Comisaría", P, UnitSubtype.Defensiva, ResourceType.Dinero, 3,
                     "El edificio más antiguo del barrio. Sobrevivió cuatro gobiernos y una inundación."),
                Unit("subsidio", "Subsidio", P, UnitSubtype.Productora, ResourceType.Dinero, 5,
                     "El Estado se financia a sí mismo. Sostenible, dicen.",
                     Produce(ResourceType.Dinero, 1)),
                Unit("gorra_barrio", "Gorra de Barrio", P, UnitSubtype.Productora, ResourceType.Fuerza, 3,
                     "Lo conoce todo el mundo. Nadie sabe exactamente qué hace. Siempre está.",
                     Produce(ResourceType.Fuerza, 1)),
                Unit("conferencia", "Conferencia de Prensa", P, UnitSubtype.Productora, ResourceType.Social, 5,
                     "El ministro sonríe. Los periodistas anotan. Nadie pregunta nada difícil.",
                     Produce(ResourceType.Social, 1)),

                Action("partida", "Partida Presupuestaria", P, ActionCategory.Boost, ResourceType.Social, 1,
                       "Existe en el papel. Se aprobó a las 3 AM. Nadie sabe bien para qué.",
                       ModRes(TargetType.Self, ResourceType.Dinero, 7)),
                Action("licitacion", "Licitación Express", P, ActionCategory.Boost, ResourceType.Dinero, 3,
                       "Una empresa, un sobre, 48 horas. El pliego lo hicieron el lunes.",
                       ModRes(TargetType.Self, ResourceType.Fuerza, 8)),
                Action("cadena_nacional", "Cadena Nacional", P, ActionCategory.Boost, ResourceType.Fuerza, 2,
                       "Interrumpe la novela. El presidente habla 40 minutos. Nadie pidió que pare.",
                       ModRes(TargetType.Self, ResourceType.Social, 4)),

                Action("embargo", "Embargo", P, ActionCategory.Sabotaje, ResourceType.Fuerza, 3,
                       "El juez firmó. La plata se fue. El afectado ya lo sospechaba.",
                       ModRes(TargetType.Opponent, ResourceType.Dinero, -7)),
                Action("detencion", "Detención", P, ActionCategory.Sabotaje, ResourceType.Dinero, 1,
                       "Demorado para averiguación de antecedentes. O por las dudas. Principalmente por las dudas.",
                       ModRes(TargetType.Opponent, ResourceType.Fuerza, -3)),
                Action("censura", "Censura", P, ActionCategory.Sabotaje, ResourceType.Social, 2,
                       "El artículo fue dado de baja. Por razones técnicas. Técnicas.",
                       ModRes(TargetType.Opponent, ResourceType.Social, -5)),
                Action("infiltrado", "Infiltrado", P, ActionCategory.Sabotaje, ResourceType.Dinero, 4,
                       "Un tipo raro en la marcha. Nadie lo conoce pero todos lo sospechaban.",
                       ModHP(TargetType.Opponent, -1)),

                Action("operativo", "Operativo Apretón", P, ActionCategory.Ataque, ResourceType.Dinero, 6,
                       "Cuatro camiones, veinte efectivos, un drone. Para un jubilado con un cartel.",
                       ModHP(TargetType.Opponent, -18)),
                Action("balas_goma", "Balas de Goma", P, ActionCategory.Defensa, ResourceType.Social, 5,
                       "No matan, dicen. Técnicamente. El protocolo fue actualizado en 2019.",
                       ModHP(TargetType.Self, 12)),

                Action("toque_queda", "Toque de Queda", P, ActionCategory.EfectoEspecial, ResourceType.Dinero, 5,
                       "A las 22hs, todos adentro. El que salga averigua.",
                       ApplyStatus(TargetType.Opponent, Skip())),
                Action("decreto", "Decreto de Emergencia", P, ActionCategory.EfectoEspecial, ResourceType.Dinero, 3,
                       "El Congreso estaba de feria. Había urgencia. Siempre hay urgencia.",
                       ApplyStatus(TargetType.Self, Double())),
            };
        }

        /// <summary>Ids de las unidades iniciales de cada facción (spec §6). 1 peleadora + 1 productora.</summary>
        public static string[] StartingUnitIds(Faction faction) =>
            faction == Faction.Manifestantes
                ? new[] { "piquetero", "olla_popular" }
                : new[] { "patrullero", "subsidio" };

        // ── Builders ──────────────────────────────────────────────────────────

        private static UnitCardData Unit(string id, string name, Faction faction, UnitSubtype sub,
                                         ResourceType costResource, int cost, string flavor,
                                         params PassiveEffect[] passives)
        {
            var card = ScriptableObject.CreateInstance<UnitCardData>();
            card.name = id;
            card.id = id;
            card.cardName = name;
            card.faction = faction;
            card.unitSubtype = sub;
            card.costs = SingleCost(costResource, cost);
            card.maxHp = BaselineHp;
            card.allowedSlots = Array.Empty<int>();           // cualquiera (baseline)
            card.attack = BaselineAttack();
            card.passiveEffects = new List<PassiveEffect>(passives);
            card.flavorText = flavor;
            return card;
        }

        private static ActionCardData Action(string id, string name, Faction faction, ActionCategory cat,
                                             ResourceType costResource, int cost, string flavor,
                                             params CardEffect[] effects)
        {
            var card = ScriptableObject.CreateInstance<ActionCardData>();
            card.name = id;
            card.id = id;
            card.cardName = name;
            card.faction = faction;
            card.actionCategory = cat;
            card.costs = SingleCost(costResource, cost);
            card.flavorText = flavor;
            card.effects = new List<CardEffect>(effects);
            return card;
        }

        // Baseline (spec §9/§10): elige 1 de los 6 slots del oponente, 5 de daño.
        private static UnitAttack BaselineAttack() =>
            new UnitAttack(AttackReference.Absolute, new[] { 0, 1, 2, 3, 4, 5 }, 1, BaselineDamage);

        private static List<ResourceCost> SingleCost(ResourceType r, int amount) =>
            new List<ResourceCost> { new ResourceCost(r, amount) };

        private static PassiveEffect Produce(ResourceType r, int v) =>
            new PassiveEffect(PassiveType.ProduceResource, r, v);

        private static CardEffect ModHP(TargetType t, int v) => new CardEffect(CardEffectType.ModifyHP, t, value: v);
        private static CardEffect ModRes(TargetType t, ResourceType r, int v) => new CardEffect(CardEffectType.ModifyResource, t, r, v);
        private static CardEffect ApplyStatus(TargetType t, StatusEffect s) => new CardEffect(CardEffectType.ApplyStatus, t, status: s);

        private static StatusEffect Skip() => new StatusEffect(StatusType.SkipProduction, 0, 1);
        private static StatusEffect Double() => new StatusEffect(StatusType.DoubleProduction, 2, 1);
    }
}

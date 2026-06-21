using System;
using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Fuente de verdad de las cartas, en código (valores del spec §9/§10 + flavor). La usa el
    /// generador de assets del editor (las persiste como ScriptableObjects). Cambiar una carta =
    /// cambiarla acá y regenerar.
    ///
    /// Los valores numéricos están <b>validados por simulación</b> (Fase 5, `sim/`): incorporan el
    /// tune global de durabilidad (daño ×1.5 / HP ×0.85) y los ajustes per-card de balance.
    /// Catálogo: 8 unidades + 10 acciones + 4 equipo = 22/facción. Índices de slot en base 0
    /// (slot k del spec = índice k-1); los offsets Relative no cambian.
    /// </summary>
    public static class CardLibrary
    {
        private static readonly int[] Retaguardia = { 0, 1, 2 };
        private static readonly int[] Frente = { 3, 4, 5 };
        private static readonly int[] Medio = { 1, 2, 3, 4 };

        public static List<CardData> BuildManifestantes()
        {
            const Faction M = Faction.Manifestantes;
            return new List<CardData>
            {
                // ── Unidades ──
                Unit("piquetero", "Piquetero", M, UnitSubtype.Atacante, ResourceType.Fuerza, 4,
                     20, NoSlots, Atk(AttackReference.Relative, new[] { -1, 0, 1 }, 1, 14),
                     "Bombo, bandera y aguante para parar todo. El GPS del camionero lo putea de memoria.",
                     Aura(2)),
                Unit("jubilado", "Jubilado", M, UnitSubtype.Defensiva, ResourceType.Dinero, 5,
                     32, Frente, Atk(AttackReference.Absolute, new[] { 3, 4, 5 }, 0, 4),
                     "83 pirulos, bastón y primera fila. La cana le tiene cagazo a lo que largue en la tele.",
                     Espinas(3)),
                Unit("gordo_sindical", "Gordo Sindical", M, UnitSubtype.Productora, ResourceType.Dinero, 3,
                     12, Retaguardia, Atk(AttackReference.Relative, new[] { 0 }, 0, 3),
                     "El que arregla la paritaria y maneja la caja. Aparece en el palco, jamás en la primera fila.",
                     Produce(ResourceType.Dinero, 1)),
                Unit("fisura", "Fisura", M, UnitSubtype.Atacante, ResourceType.Fuerza, 5,
                     20, Medio, Atk(AttackReference.Relative, new[] { -1, 0, 1 }, 0, 6),
                     "Arranca la baldosa de la plaza con las manos y la parte en cuatro. Cada cascote tiene destinatario.",
                     Produce(ResourceType.Fuerza, 1)),
                Unit("tuitero", "Tuitero Militante", M, UnitSubtype.Productora, ResourceType.Social, 2,
                     10, Retaguardia, Atk(AttackReference.Relative, new[] { 0 }, 0, 2),
                     "2.300 seguidores y la certeza de que cambió la historia con un hilo.",
                     Produce(ResourceType.Social, 1)),
                Unit("choripanero", "Choripanero", M, UnitSubtype.Defensiva, ResourceType.Social, 4,
                     15, Medio, Heal(AttackReference.Absolute, new[] { 3, 4, 5 }, 1, 3),
                     "Pan, chori y chimi para aguantar la jornada. El que morfa, vuelve a la marcha."),
                Unit("mortero", "Mortero Casero", M, UnitSubtype.Atacante, ResourceType.Fuerza, 5,
                     8, new[] { 1, 2, 3 }, Atk(AttackReference.Absolute, new[] { 0, 1, 2 }, 1, 14),
                     "Un caño, pólvora trucha y puntería de chiripa. Igual le encaja justo en la oficina del fondo."),
                Unit("quema_cubiertas", "Quema de Cubiertas", M, UnitSubtype.Atacante, ResourceType.Social, 5,
                     15, Medio, Atk(AttackReference.Relative, new[] { 0 }, 0, 2),
                     "Diez gomas viejas y el viento a favor. El humo negro no le hace asco a nadie.",
                     Humo(2)),

                // ── Acciones ──
                Action("colecta", "Colecta", M, ActionCategory.Boost, ResourceType.Social, 3,
                       "Pasamos la gorra. La de los compañeros, no la de la cana.",
                       ModRes(TargetType.Self, ResourceType.Dinero, 6)),
                Action("fernet", "Fernet con Cola", M, ActionCategory.Boost, ResourceType.Dinero, 1,
                       "Hidratación táctica. No es doping si lo toma toda la marcha.",
                       ModRes(TargetType.Self, ResourceType.Fuerza, 3)),
                Action("viral", "Viral en Redes", M, ActionCategory.Boost, ResourceType.Dinero, 2,
                       "Un video de 14 segundos, tres palos de reproducciones. El ministerio ya está llamando.",
                       ModRes(TargetType.Self, ResourceType.Social, 7)),
                Action("saqueo", "Saqueo", M, ActionCategory.Sabotaje, ResourceType.Fuerza, 1,
                       "No es afano. Es redistribución urgente de mercadería.",
                       ModRes(TargetType.Opponent, ResourceType.Dinero, -3)),
                Action("paro_general", "Paro General", M, ActionCategory.Ataque, ResourceType.Fuerza, 5,
                       "24 horas de nada. No hay bondi, no hay banco, no hay delivery. El país clavado.",
                       ModHP(TargetType.Opponent, -21)),
                Action("abrazo", "Abrazo Colectivo", M, ActionCategory.Defensa, ResourceType.Dinero, 5,
                       "El abrazo que cura todo. Menos la deuda en pesos.",
                       ModHP(TargetType.Self, 10)),
                Action("asamblea", "Asamblea Popular", M, ActionCategory.EfectoEspecial, ResourceType.Social, 6,
                       "Se vota a mano alzada. Cuatro horas de bardo, pero esta vez salió.",
                       ApplyStatus(TargetType.Self, Double())),
                Action("escrache", "Escrache", M, ActionCategory.Sabotaje, ResourceType.Social, 4,
                       "Le golpean la puerta a las 7 de la mañana con bombos. No se asoma en todo el día.",
                       ApplyStatus(TargetType.Opponent, Stun())),
                Action("el_aguante", "El Aguante", M, ActionCategory.Boost, ResourceType.Fuerza, 2,
                       "Cantito, bombo y se renueva el aguante. Treinta cuadras más, fácil.",
                       ApplyStatus(TargetType.Self, Furia(4, 2))),
                Action("cambio_consigna", "Cambio de Consigna", M, ActionCategory.EfectoEspecial, ResourceType.Social, 1,
                       "La columna pega la vuelta en U. Nadie cazó la orden, pero todos giraron.",
                       Move()),

                // ── Equipo ──
                Equipment("pechera", "Pechera de Cartón", M, ResourceType.Dinero, 3,
                          "Cartón, cinta de embalar y fe. Aguanta más de lo que el sentido común permite.",
                          new[] { MaxHpMod(10) }, NoPassives),
                Equipment("cascote", "Cascote", M, ResourceType.Fuerza, 2,
                          "El fierro más democrático: gratis, abundante y siempre a mano.",
                          new[] { DamageMod(4) }, NoPassives),
                Equipment("parrilla", "Parrilla Portátil", M, ResourceType.Dinero, 3,
                          "Media parrilla, una bolsa de carbón y olor a asado. Cura lo que ninguna obra social.",
                          NoMods, new[] { Regen(2) }),
                Equipment("miguelitos", "Miguelitos", M, ResourceType.Fuerza, 3,
                          "Tres clavos soldados con saña. El patrullero los encuentra tarde, siempre.",
                          NoMods, new[] { Espinas(4) }),
            };
        }

        public static List<CardData> BuildPolicias()
        {
            const Faction P = Faction.Policias;
            return new List<CardData>
            {
                // ── Unidades ──
                Unit("infante", "Infante", P, UnitSubtype.Atacante, ResourceType.Fuerza, 6,
                     22, NoSlots, Atk(AttackReference.Relative, new[] { -1, 0, 1 }, 1, 15),
                     "Escudo, casco y 14 horas de turno. Va al frente porque le pagan para eso.",
                     Aura(2)),
                Unit("gendarme", "Gendarme", P, UnitSubtype.Defensiva, ResourceType.Dinero, 4,
                     27, Frente, Atk(AttackReference.Absolute, new[] { 3, 4, 5 }, 0, 3),
                     "Lo trajeron de la frontera a cuidar una esquina. No se mueve, no se cansa, no entiende el reclamo.",
                     Espinas(3)),
                Unit("puntero", "Puntero", P, UnitSubtype.Productora, ResourceType.Dinero, 5,
                     12, Retaguardia, Atk(AttackReference.Relative, new[] { 0 }, 0, 3),
                     "Reparte bolsones y promesas. La guita sale de algún lado, siempre.",
                     Produce(ResourceType.Dinero, 1)),
                Unit("itakero", "Itakero", P, UnitSubtype.Atacante, ResourceType.Fuerza, 4,
                     19, Medio, Atk(AttackReference.Relative, new[] { -1, 0, 1 }, 0, 4),
                     "Escopeta Itaka y postas de goma. Apunta al montón, total alguno cae.",
                     Produce(ResourceType.Fuerza, 1)),
                Unit("trol", "Trol Oficial", P, UnitSubtype.Productora, ResourceType.Social, 5,
                     14, Retaguardia, Atk(AttackReference.Relative, new[] { 0 }, 0, 2),
                     "Diez cuentas, un solo sueldo del Estado. Inventa la tendencia antes del mediodía.",
                     Produce(ResourceType.Social, 1)),
                Unit("medico_same", "Médico del SAME", P, UnitSubtype.Defensiva, ResourceType.Dinero, 4,
                     15, Medio, Heal(AttackReference.Relative, new[] { -1, 0, 1 }, 0, 2),
                     "Llega en ambulancia y atiende a todos. Después hace tres guardias para llegar a fin de mes."),
                Unit("halcon", "Halcón", P, UnitSubtype.Atacante, ResourceType.Fuerza, 6,
                     8, new[] { 1, 2, 3 }, Atk(AttackReference.Absolute, new[] { 0, 1, 2 }, 1, 15),
                     "Grupo especial, mira telescópica y paciencia de cazador. Desde la terraza ve toda la plaza."),
                Unit("gasero", "Gasero", P, UnitSubtype.Atacante, ResourceType.Social, 5,
                     15, Medio, Atk(AttackReference.Relative, new[] { 0 }, 0, 2),
                     "Granada en mano, pañuelo en la cara. \"Es para dispersar\", dice, mientras llora hasta él.",
                     Gas(3)),

                // ── Acciones ──
                Action("partida", "Partida Presupuestaria", P, ActionCategory.Boost, ResourceType.Social, 2,
                       "Existe en el papel. Se aprobó a las 3 de la mañana y nadie sabe para qué.",
                       ModRes(TargetType.Self, ResourceType.Dinero, 7)),
                Action("licitacion", "Licitación Express", P, ActionCategory.Boost, ResourceType.Dinero, 3,
                       "Una empresa, un sobre y 48 horas. El pliego lo hicieron el lunes a la tarde.",
                       ModRes(TargetType.Self, ResourceType.Fuerza, 8)),
                Action("cadena", "Cadena Nacional", P, ActionCategory.Boost, ResourceType.Dinero, 2,
                       "Interrumpe la novela. El presidente habla 40 minutos. Nadie pidió que arranque.",
                       ModRes(TargetType.Self, ResourceType.Social, 4)),
                Action("embargo", "Embargo", P, ActionCategory.Sabotaje, ResourceType.Fuerza, 3,
                       "El juez firmó, la guita voló. El otro ya lo veía venir.",
                       ModRes(TargetType.Opponent, ResourceType.Dinero, -7)),
                Action("operativo", "Operativo Apretón", P, ActionCategory.Ataque, ResourceType.Dinero, 6,
                       "Cuatro camiones, veinte efectivos y un drone. Todo para un jubilado con un cartel.",
                       ModHP(TargetType.Opponent, -27)),
                Action("refuerzos", "Refuerzos", P, ActionCategory.Defensa, ResourceType.Social, 5,
                       "Llegan dos camiones más. La línea se rearma como si nada.",
                       ModHP(TargetType.Self, 8)),
                Action("toque_queda", "Toque de Queda", P, ActionCategory.EfectoEspecial, ResourceType.Dinero, 5,
                       "A las 22 todos adentro. El que se manda afuera, va en cana.",
                       ApplyStatus(TargetType.Opponent, Skip())),
                Action("causa_judicial", "Causa Judicial", P, ActionCategory.Sabotaje, ResourceType.Dinero, 4,
                       "Te arman un expediente. Te va comiendo de a poco, durante años.",
                       ApplyStatus(TargetType.Opponent, Poison(3, 2))),
                Action("apriete", "Apriete", P, ActionCategory.Sabotaje, ResourceType.Fuerza, 2,
                       "Una charla en voz baja contra la pared. Se te van las ganas solas.",
                       ApplyStatus(TargetType.Opponent, Desmor(4, 2))),
                Action("reubicacion", "Reubicación Forzosa", P, ActionCategory.EfectoEspecial, ResourceType.Dinero, 2,
                       "Los suben a un patrullero, los bajan en la otra punta. Protocolo, dicen.",
                       Swap()),

                // ── Equipo ──
                Equipment("chaleco", "Chaleco Antibalas", P, ResourceType.Dinero, 3,
                          "Importado. Al menos figura en el inventario, que ya es algo.",
                          new[] { MaxHpMod(12) }, NoPassives),
                Equipment("tonfa", "Tonfa", P, ResourceType.Fuerza, 2,
                          "Reglamentaria. El uso, a criterio del que la empuña.",
                          new[] { DamageMod(4) }, NoPassives),
                Equipment("obra_social", "Obra Social", P, ResourceType.Dinero, 3,
                          "Cobertura del 100%. Después de tres formularios y una mañana de cola.",
                          NoMods, new[] { Regen(2) }),
                Equipment("reflectores", "Reflectores", P, ResourceType.Social, 2,
                          "Iluminan todo de golpe. De repente la patota se coordina sola.",
                          NoMods, new[] { Aura(2) }),
            };
        }

        /// <summary>Ids de las unidades iniciales de cada facción (spec §6/§11.3). 1 peleadora + 1 productora.</summary>
        public static string[] StartingUnitIds(Faction faction) =>
            faction == Faction.Manifestantes
                ? new[] { "piquetero", "gordo_sindical" }
                : new[] { "infante", "puntero" };

        // ── Builders ──────────────────────────────────────────────────────────

        private static readonly int[] NoSlots = Array.Empty<int>();          // cualquiera
        private static readonly PassiveEffect[] NoPassives = Array.Empty<PassiveEffect>();
        private static readonly StatModifier[] NoMods = Array.Empty<StatModifier>();

        private static UnitCardData Unit(string id, string name, Faction faction, UnitSubtype sub,
                                         ResourceType costResource, int cost, int maxHp, int[] allowed,
                                         UnitAttack attack, string flavor, params PassiveEffect[] passives)
        {
            var card = ScriptableObject.CreateInstance<UnitCardData>();
            card.name = id;
            card.id = id;
            card.cardName = name;
            card.faction = faction;
            card.unitSubtype = sub;
            card.costs = SingleCost(costResource, cost);
            card.maxHp = maxHp;
            card.allowedSlots = allowed;
            card.attack = attack;
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

        private static EquipmentCardData Equipment(string id, string name, Faction faction,
                                                   ResourceType costResource, int cost, string flavor,
                                                   StatModifier[] mods, PassiveEffect[] passives)
        {
            var card = ScriptableObject.CreateInstance<EquipmentCardData>();
            card.name = id;
            card.id = id;
            card.cardName = name;
            card.faction = faction;
            card.costs = SingleCost(costResource, cost);
            card.flavorText = flavor;
            card.statModifiers = new List<StatModifier>(mods);
            card.grantedPassives = new List<PassiveEffect>(passives);
            return card;
        }

        // Ataques
        private static UnitAttack Atk(AttackReference reference, int[] pattern, int pick, int dmg) =>
            new UnitAttack(reference, pattern, pick, dmg, AttackEffect.DamageEnemies);

        private static UnitAttack Heal(AttackReference reference, int[] pattern, int pick, int amount) =>
            new UnitAttack(reference, pattern, pick, amount, AttackEffect.HealAllies);

        // Pasivas
        private static PassiveEffect Produce(ResourceType r, int v) =>
            new PassiveEffect(PassiveType.ProduceResource, r, v);

        private static PassiveEffect Aura(int v) => new PassiveEffect
        {
            passiveType = PassiveType.AuraDamage, value = v, target = PassiveTarget.Allies,
            reference = AttackReference.Relative, pattern = new[] { -1, 1 }, pickCount = 0
        };

        private static PassiveEffect Espinas(int v) => new PassiveEffect
        {
            passiveType = PassiveType.Retaliate, value = v, target = PassiveTarget.Self
        };

        private static PassiveEffect Regen(int v) => new PassiveEffect
        {
            passiveType = PassiveType.Regeneration, value = v, target = PassiveTarget.Self
        };

        private static PassiveEffect Humo(int v) => new PassiveEffect
        {
            passiveType = PassiveType.TurnDamage, value = v, target = PassiveTarget.Enemies,
            reference = AttackReference.Absolute, pattern = new[] { 3, 4, 5 }, pickCount = 0
        };

        // Gas: Veneno a 1 de la vanguardia enemiga (pick 1; la resolución de pasivas elige N
        // slots ocupados, determinista). Ver spec §7.3/§10.
        private static PassiveEffect Gas(int v) => new PassiveEffect
        {
            passiveType = PassiveType.TurnStatus, status = Poison(v, 1), target = PassiveTarget.Enemies,
            reference = AttackReference.Absolute, pattern = new[] { 3, 4, 5 }, pickCount = 1
        };

        // Efectos de acción
        private static CardEffect ModHP(TargetType t, int v) => new CardEffect(CardEffectType.ModifyHP, t, value: v);
        private static CardEffect ModRes(TargetType t, ResourceType r, int v) => new CardEffect(CardEffectType.ModifyResource, t, r, v);
        private static CardEffect ApplyStatus(TargetType t, StatusEffect s) => new CardEffect(CardEffectType.ApplyStatus, t, status: s);
        private static CardEffect Move() => new CardEffect(CardEffectType.MoveUnit, TargetType.Self);
        private static CardEffect Swap() => new CardEffect(CardEffectType.SwapUnits, TargetType.Opponent);

        // Estados
        private static StatusEffect Skip() => new StatusEffect(StatusType.SkipProduction, 0, 1);
        private static StatusEffect Double() => new StatusEffect(StatusType.DoubleProduction, 2, 1);
        private static StatusEffect Stun() => new StatusEffect(StatusType.Stun, 0, 1);
        private static StatusEffect Furia(int v, int c) => new StatusEffect(StatusType.Furia, v, c);
        private static StatusEffect Poison(int v, int c) => new StatusEffect(StatusType.Poison, v, c);
        private static StatusEffect Desmor(int v, int c) => new StatusEffect(StatusType.Desmoralizar, v, c);

        // Modificadores de equipo
        private static StatModifier MaxHpMod(int v) => new StatModifier(StatType.MaxHp, v);
        private static StatModifier DamageMod(int v) => new StatModifier(StatType.Damage, v);

        private static List<ResourceCost> SingleCost(ResourceType r, int amount) =>
            new List<ResourceCost> { new ResourceCost(r, ScaleCost(amount)) };

        /// <summary>
        /// Factor económico global de costos (spec §3): todas las cartas cuestan un poco más,
        /// para que los recursos no sobren. Se aplica al <b>hornear</b> los assets (tiempo de
        /// generación, no en runtime), así el asset queda con el costo final. Es el espejo de
        /// <c>knobs.SHIPPED.cost_mult</c> en el simulador — mantener AMBOS en sync.
        /// Los <c>amount</c> de los builders de arriba son el costo <b>base de diseño</b>
        /// (per-card, balanceado por valor/costo); este factor los escala parejo.
        /// </summary>
        private const float CostScale = 1.2f;

        private static int ScaleCost(int amount)
        {
            int scaled = (int)Math.Round(amount * CostScale, MidpointRounding.ToEven);
            return scaled < 1 ? 1 : scaled;   // piso 1 (igual que sim.scale minimum=1)
        }
    }
}

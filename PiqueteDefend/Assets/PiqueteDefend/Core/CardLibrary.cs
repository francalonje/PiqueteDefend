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
    /// Los valores numéricos son <b>rough</b> (anclas de diseño, §6.1): PENDIENTES de re-balanceo por
    /// sim/playtest tras el rework de cartas. Catálogo: 9 unidades + 8 acciones + 4 equipo = 21/facción.
    /// Roster <b>asimétrico</b> (§6.1 #11): las pasivas distintivas NO se repiten entre facciones.
    /// allowedSlots en base 0 (slot k del spec = índice k-1); el targeting de ataque es por
    /// <see cref="TargetMode"/>, anclado a la formación (spec §6) — el frente es el extremo de índice alto.
    ///
    /// <b>Recurso de costo por tipo (spec §3):</b> Unidad → $ Dinero · Acción y Equipo → 📣 Social.
    /// Atacar cuesta ⚡ Fuerza (no es costo de carta, §6/GameConfig). Lo per-card es el <b>monto</b>.
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
                // ── Unidades (9) — cuestan $ Dinero (spec §3) ──
                Unit("piquetero", "Piquetero", M, UnitSubtype.Atacante, 4,
                     20, NoSlots, Atk(TargetMode.Frontmost, 1, 14),
                     "Bombo, bandera y aguante para parar el país. El camionero lo putea en seis idiomas y él ni se inmuta.",
                     Aura(2)),
                Unit("fisura", "Fisura", M, UnitSubtype.Atacante, 5,
                     18, Medio, Atk(TargetMode.Frontmost, 3, 7),
                     "Arranca la baldosa con las manos y la parte en cuatro. Cada cascote ya tiene nombre y apellido.",
                     Produce(ResourceType.Fuerza, 1)),
                Unit("jubilado", "Jubilado", M, UnitSubtype.Atacante, 2,
                     6, NoSlots, Atk(TargetMode.Any, 1, 2),
                     "Mil miércoles de marcha en el lomo y cero miedo a esta altura del partido. Cuando cae, la columna redobla el bombo y sale con todo.",
                     OnDeathFuria(4, 2)),
                Unit("mortero", "Mortero Casero", M, UnitSubtype.Atacante, 5,
                     8, new[] { 1, 2, 3 }, Atk(TargetMode.Backmost, 1, 14),
                     "Un caño, pólvora trucha y fe. No le apunta a nadie, pero siempre le encaja al de la oficina del fondo."),
                Unit("encadenado", "Encadenado", M, UnitSubtype.Defensiva, 5,
                     32, Frente, Atk(TargetMode.Frontmost, 2, 3),
                     "Se candó al obelisco a las seis de la mañana y tiró la llave. De ahí no lo saca nadie, y el que lo intenta se lleva los candados de recuerdo.",
                     Espinas(3)),
                Unit("gordo_sindical", "Gordo Sindical", M, UnitSubtype.Productora, 3,
                     12, Retaguardia, Atk(TargetMode.Frontmost, 1, 3),
                     "Maneja la caja, la lista y el micro. Aparece en el palco, jamás en la primera fila.",
                     Produce(ResourceType.Dinero, 2)),
                Unit("choripanero", "Choripanero", M, UnitSubtype.Defensiva, 4,
                     15, Medio, Heal(TargetMode.Any, 1, 3),
                     "Pan, chori y un chimi que resucita muertos. El que morfa, vuelve a la marcha como si nada."),
                Unit("tuitero", "Tuitero Militante", M, UnitSubtype.Productora, 2,
                     10, Retaguardia, Atk(TargetMode.Frontmost, 1, 2),
                     "2.300 seguidores y la certeza absoluta de que cambió la historia con un hilo de Twitter.",
                     Produce(ResourceType.Social, 2)),
                Unit("quema_cubiertas", "Quema de Cubiertas", M, UnitSubtype.Atacante, 5,
                     15, Medio, Atk(TargetMode.Any, 1, 2),
                     "Diez gomas viejas, un fósforo y el viento a favor. El humo negro no discrimina: te entra a todos.",
                     Humo(2)),

                // ── Acciones (8) — cuestan 📣 Social (spec §3) ──
                Action("colecta", "Colecta", M, ActionCategory.Boost, 3,
                       "Pasamos la gorra. La de los compañeros, no la de la cana.",
                       ModRes(TargetType.Self, ResourceType.Dinero, 6)),
                Action("fernet", "Fernet con Cola", M, ActionCategory.Boost, 1,
                       "Hidratación táctica. No es doping si lo toma toda la marcha.",
                       ModRes(TargetType.Self, ResourceType.Fuerza, 3)),
                Action("viral", "Viral en Redes", M, ActionCategory.Boost, 2,
                       "Catorce segundos de video, tres palos de reproducciones. El ministerio ya está llamando.",
                       ModRes(TargetType.Self, ResourceType.Social, 7)),
                Action("paro_general", "Paro General", M, ActionCategory.Ataque, 5,
                       "24 horas de nada. No hay bondi, no hay banco, no hay delivery. El país clavado.",
                       ModHP(TargetType.Opponent, -21)),
                Action("el_aguante", "El Aguante", M, ActionCategory.Boost, 2,
                       "Cantito, bombo y se renueva el aguante. Treinta cuadras más, fácil.",
                       ApplyStatus(TargetType.Self, Furia(4, 2))),
                Action("asamblea", "Asamblea Popular", M, ActionCategory.EfectoEspecial, 6,
                       "Se vota a mano alzada. Cuatro horas de bardo, pero esta vez salió.",
                       ApplyStatus(TargetType.Self, Double())),
                Action("abrazo", "Abrazo Colectivo", M, ActionCategory.Defensa, 5,
                       "El abrazo que cura todo. Menos la deuda en pesos.",
                       ModHP(TargetType.Self, 10)),
                Action("escrache", "Escrache", M, ActionCategory.Sabotaje, 4,
                       "Le golpean la puerta a las 7 de la mañana con bombos. No se asoma en todo el día.",
                       ApplyStatus(TargetType.Opponent, Stun())),

                // ── Equipo (4) — cuestan 📣 Social (spec §3) ──
                Equipment("pechera", "Pechera de Cartón", M, 3,
                          "Cartón, cinta de embalar y fe. Aguanta más de lo que el sentido común permite.",
                          new[] { MaxHpMod(10) }, NoPassives),
                Equipment("cascote", "Cascote", M, 2,
                          "El fierro más democrático: gratis, abundante y siempre a mano.",
                          new[] { DamageMod(4) }, NoPassives),
                Equipment("parrilla", "Parrilla Portátil", M, 3,
                          "Media parrilla, una bolsa de carbón y olor a asado. Cura lo que ninguna obra social.",
                          NoMods, new[] { Regen(2) }),
                Equipment("miguelitos", "Miguelitos", M, 3,
                          "Tres clavos soldados con saña. El patrullero los encuentra tarde, siempre.",
                          NoMods, new[] { Espinas(4) }),
            };
        }

        public static List<CardData> BuildPolicias()
        {
            const Faction P = Faction.Policias;
            return new List<CardData>
            {
                // ── Unidades (9) — cuestan $ Dinero (spec §3) ──
                Unit("infante", "Infante", P, UnitSubtype.Atacante, 5,
                     24, NoSlots, Atk(TargetMode.Frontmost, 1, 14),
                     "Casco, escudo y cara de pocas pulgas. Va al frente porque es lo que mejor hace: plantarse y no moverse ni con grúa."),
                Unit("itakero", "Itakero", P, UnitSubtype.Atacante, 4,
                     20, Medio, Atk(TargetMode.Frontmost, 3, 4),
                     "Escopeta Itaka y postas de goma para todos. Apunta al montón y reza, total alguno cae."),
                Unit("halcon", "Halcón", P, UnitSubtype.Atacante, 6,
                     8, new[] { 1, 2, 3 }, Atk(TargetMode.Any, 1, 15),
                     "Mira telescópica desde la terraza. Te tiene en la cruz desde antes de que llegaras a la esquina."),
                Unit("gendarme", "Gendarme", P, UnitSubtype.Defensiva, 5,
                     26, Frente, Atk(TargetMode.Frontmost, 2, 4),
                     "Lo trajeron de la frontera a cuidar una baldosa, y la cuida con la vida. No se mueve, no se cansa, no afloja.",
                     Blindaje(2)),
                Unit("carro_hidrante", "Carro Hidrante", P, UnitSubtype.Atacante, 4,
                     18, Medio, Atk(TargetMode.Any, 1, 3),
                     "Diez mil litros a presión. Te despega del asfalto y te deja en la otra cuadra antes de que termines el cántico.",
                     Chorro()),
                Unit("recaudador", "Recaudador", P, UnitSubtype.Productora, 3,
                     12, Retaguardia, Atk(TargetMode.Frontmost, 1, 3),
                     "La plata sale de algún lado y mejor no preguntes. Reparte sobres y se queda con el vuelto.",
                     Produce(ResourceType.Dinero, 2)),
                Unit("caballeria", "Caballería", P, UnitSubtype.Atacante, 6,
                     16, Medio, Atk(TargetMode.All, 0, 2),
                     "Entran al galope y a lo que venga. El comunicado oficial lo tituló \"reordenamiento dinámico del espacio público\"."),
                Unit("trol", "Trol Oficial", P, UnitSubtype.Productora, 3,
                     14, Retaguardia, Atk(TargetMode.Frontmost, 1, 2),
                     "Diez cuentas, un solo sueldo del Estado y cero ortografía. Inventa la tendencia antes del café.",
                     Produce(ResourceType.Social, 2)),
                Unit("gasero", "Gasero", P, UnitSubtype.Atacante, 5,
                     15, Medio, Atk(TargetMode.Frontmost, 1, 2),
                     "Granada de gas en una mano, pañuelo en la otra. \"Es para dispersar\", avisa, y la nube no lee carteles: dispersa la plaza, la esquina y el kiosco de paso.",
                     Gas(2)),

                // ── Acciones (8) — cuestan 📣 Social (spec §3) ──
                Action("partida", "Partida Presupuestaria", P, ActionCategory.Boost, 2,
                       "Existe en el papel. Se aprobó a las 3 de la mañana y nadie sabe para qué.",
                       ModRes(TargetType.Self, ResourceType.Dinero, 7)),
                Action("licitacion", "Licitación Express", P, ActionCategory.Boost, 3,
                       "Una empresa, un sobre y 48 horas. El pliego lo hicieron el lunes a la tarde.",
                       ModRes(TargetType.Self, ResourceType.Fuerza, 10)),
                Action("cadena", "Cadena Nacional", P, ActionCategory.Boost, 2,
                       "Interrumpe la novela. El presidente habla 40 minutos. Nadie pidió que arranque.",
                       ModRes(TargetType.Self, ResourceType.Social, 4)),
                Action("operativo", "Operativo Apretón", P, ActionCategory.Ataque, 6,
                       "Cuatro camiones, veinte efectivos y un drone. Todo para un jubilado con un cartel.",
                       ModHP(TargetType.Opponent, -27)),
                Action("causa_judicial", "Causa Judicial", P, ActionCategory.Sabotaje, 4,
                       "Te arman un expediente. Te va comiendo de a poco, durante años.",
                       ApplyStatus(TargetType.Opponent, Poison(3, 2))),
                Action("apriete", "Apriete", P, ActionCategory.Sabotaje, 2,
                       "Una charla en voz baja contra la pared. Se te van las ganas solas.",
                       ApplyStatus(TargetType.Opponent, Desmor(4, 2))),
                Action("toque_queda", "Toque de Queda", P, ActionCategory.EfectoEspecial, 5,
                       "A las 22 todos adentro. El que se manda afuera, va en cana.",
                       ApplyStatus(TargetType.Opponent, Skip())),
                Action("reubicacion", "Reubicación Forzosa", P, ActionCategory.EfectoEspecial, 2,
                       "Los suben a un patrullero, los bajan en la otra punta. Protocolo, dicen.",
                       Swap()),

                // ── Equipo (4) — cuestan 📣 Social (spec §3) ──
                Equipment("chaleco", "Chaleco Antibalas", P, 3,
                          "Importado. Al menos figura en el inventario, que ya es algo.",
                          new[] { MaxHpMod(12) }, NoPassives),
                Equipment("tonfa", "Tonfa", P, 2,
                          "Reglamentaria. El uso, a criterio del que la empuña.",
                          new[] { DamageMod(4) }, NoPassives),
                Equipment("escudo_antimotin", "Escudo Antimotín", P, 3,
                          "Policarbonato y reglamento. El palazo que da, no el que recibe.",
                          NoMods, new[] { Blindaje(2) }),
                Equipment("hidrante_mano", "Hidrante de Mano", P, 3,
                          "Versión de bolsillo del carro. Igual te despeina el cántico.",
                          NoMods, new[] { Chorro() }),
            };
        }

        /// <summary>
        /// Unidades iniciales de cada facción (spec §6/§11.3): los tres pilares de apertura —
        /// <b>Muro</b> (tanque, arranca adelante de todo), <b>Productora</b> (economía desde el turno 1)
        /// y <b>Escaramuza</b> (cuerpo ofensivo, protegido detrás del muro). El motor coloca al muro
        /// en el slot de mayor índice y a los otros dos en la retaguardia (ver <c>GameEngine.StartingSlot</c>).
        /// </summary>
        public static string[] StartingUnitIds(Faction faction) =>
            faction == Faction.Manifestantes
                ? new[] { "encadenado", "gordo_sindical", "piquetero" }   // Muro(frente) + Productora + Escaramuza
                : new[] { "gendarme", "recaudador", "infante" };

        // ── Builders ──────────────────────────────────────────────────────────

        private static readonly int[] NoSlots = Array.Empty<int>();          // cualquiera
        private static readonly PassiveEffect[] NoPassives = Array.Empty<PassiveEffect>();
        private static readonly StatModifier[] NoMods = Array.Empty<StatModifier>();

        private static UnitCardData Unit(string id, string name, Faction faction, UnitSubtype sub,
                                         int cost, int maxHp, int[] allowed,
                                         UnitAttack attack, string flavor, params PassiveEffect[] passives)
        {
            var card = ScriptableObject.CreateInstance<UnitCardData>();
            card.name = id;
            card.id = id;
            card.cardName = name;
            card.faction = faction;
            card.unitSubtype = sub;
            card.costs = SingleCost(ResourceType.Dinero, cost);   // Unidad → $ Dinero (spec §3)
            card.maxHp = maxHp;
            card.allowedSlots = allowed;
            card.attack = attack;
            card.passiveEffects = new List<PassiveEffect>(passives);
            card.flavorText = flavor;
            // Robo: las unidades pesan 1; las productoras 2 (protagonismo de producción sin tapar
            // la mano de unidades que no se pueden bajar con el tablero lleno). Espejo de cards.py.
            card.drawWeight = 1;
            foreach (PassiveEffect p in passives)
                if (p.passiveType == PassiveType.ProduceResource) { card.drawWeight = 2; break; }
            return card;
        }

        private static ActionCardData Action(string id, string name, Faction faction, ActionCategory cat,
                                             int cost, string flavor, params CardEffect[] effects)
        {
            var card = ScriptableObject.CreateInstance<ActionCardData>();
            card.name = id;
            card.id = id;
            card.cardName = name;
            card.faction = faction;
            card.actionCategory = cat;
            card.costs = SingleCost(ResourceType.Social, cost);   // Poder → 📣 Social (spec §3)
            card.flavorText = flavor;
            card.effects = new List<CardEffect>(effects);
            // Las cartas de producción (boost de recurso propio) pesan 2 en el robo. Espejo de cards.py.
            card.drawWeight = 1;
            foreach (CardEffect e in effects)
                if (e.effectType == CardEffectType.ModifyResource && e.target == TargetType.Self && e.value > 0)
                { card.drawWeight = 2; break; }
            return card;
        }

        private static EquipmentCardData Equipment(string id, string name, Faction faction,
                                                   int cost, string flavor,
                                                   StatModifier[] mods, PassiveEffect[] passives)
        {
            var card = ScriptableObject.CreateInstance<EquipmentCardData>();
            card.name = id;
            card.id = id;
            card.cardName = name;
            card.faction = faction;
            card.costs = SingleCost(ResourceType.Social, cost);   // Equipo → 📣 Social (spec §3)
            card.flavorText = flavor;
            card.statModifiers = new List<StatModifier>(mods);
            card.grantedPassives = new List<PassiveEffect>(passives);
            return card;
        }

        // Ataques (targeting anclado a la formación, spec §6)
        private static UnitAttack Atk(TargetMode mode, int count, int dmg) =>
            new UnitAttack(mode, count, dmg, AttackEffect.DamageEnemies);

        private static UnitAttack Heal(TargetMode mode, int count, int amount) =>
            new UnitAttack(mode, count, amount, AttackEffect.HealAllies);

        // Pasivas
        private static PassiveEffect Produce(ResourceType r, int v) =>
            new PassiveEffect(PassiveType.ProduceResource, r, v);

        private static PassiveEffect Aura(int v) => new PassiveEffect
        {
            passiveType = PassiveType.AuraDamage, value = v, target = PassiveTarget.Allies,
            mode = TargetMode.Adjacent, count = 0
        };

        private static PassiveEffect Espinas(int v) => new PassiveEffect
        {
            passiveType = PassiveType.Retaliate, value = v, target = PassiveTarget.Self
        };

        private static PassiveEffect Regen(int v) => new PassiveEffect
        {
            passiveType = PassiveType.Regeneration, value = v, target = PassiveTarget.Self
        };

        // Humo: daño/turno a TODO el tablero rival (AoE). Spec §7.3/§9.
        private static PassiveEffect Humo(int v) => new PassiveEffect
        {
            passiveType = PassiveType.TurnDamage, value = v, target = PassiveTarget.Enemies,
            mode = TargetMode.All, count = 0
        };

        // Gas: Veneno a TODO el tablero rival (AoE). Spec §7.3/§10.
        private static PassiveEffect Gas(int v) => new PassiveEffect
        {
            passiveType = PassiveType.TurnStatus, status = Poison(v, 1), target = PassiveTarget.Enemies,
            mode = TargetMode.All, count = 0
        };

        // OnDeath del Jubilado mártir: Furia a los aliados adyacentes al morir (spec §7.3/§9).
        private static PassiveEffect OnDeathFuria(int v, int c) => new PassiveEffect
        {
            passiveType = PassiveType.OnDeath, status = Furia(v, c), target = PassiveTarget.Allies,
            mode = TargetMode.Adjacent, count = 0
        };

        // Blindaje: reduce el daño de ataques de unidad recibido (spec §7.3, Policías).
        private static PassiveEffect Blindaje(int v) => new PassiveEffect
        {
            passiveType = PassiveType.Armor, value = v, target = PassiveTarget.Self
        };

        // Chorro: al atacar, empuja al objetivo al fondo del rival (spec §7.3, Policías).
        private static PassiveEffect Chorro() => new PassiveEffect
        {
            passiveType = PassiveType.PushBack, target = PassiveTarget.Self
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

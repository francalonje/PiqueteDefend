using System;
using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Motor de juego determinista (spec §6, §7.6). Único responsable de resolver
    /// <see cref="CardEffect"/>, procesar <see cref="StatusEffect"/> y manejar las
    /// transiciones de turno. C# puro: sin MonoBehaviours ni dependencias de escena.
    ///
    /// Reproduce exactamente la lógica de tools/balance_sim/simulator.py, pero
    /// reestructurada como máquina de estados: en vez de pedirle la acción a una IA,
    /// expone fases (<see cref="Phase"/>) y espera que la presentación llame
    /// <see cref="BeginTurn"/> y luego <see cref="PlayCard"/> o <see cref="DiscardCard"/>.
    ///
    /// Convención de turnos: <see cref="HalfTurn"/> cuenta turnos individuales (uno por
    /// jugador). <c>suddenDeathStart</c> y <c>maxTurns</c> se miden en esa unidad.
    /// </summary>
    public sealed class GameEngine
    {
        private readonly GameConfig _config;
        private readonly IRandomProvider _rng;
        private readonly ICardCatalog _catalog;

        private readonly PlayerState[] _players = new PlayerState[2];
        private int _activeIndex;
        private int _firstIndex;
        private GameOutcome? _outcome;

        public GameEngine(GameConfig config, IRandomProvider rng, ICardCatalog catalog)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Phase = GamePhase.NotStarted;
        }

        // ── Estado observable ───────────────────────────────────────────────────

        public GameConfig Config => _config;
        public GamePhase Phase { get; private set; }
        public int HalfTurn { get; private set; }
        public int ActiveIndex => _activeIndex;
        public int FirstIndex => _firstIndex;
        public GameOutcome? Outcome => _outcome;
        public bool IsFinished => _outcome.HasValue;

        public PlayerState PlayerAt(int index) => _players[index];
        public PlayerState ActivePlayer => _players[_activeIndex];
        public PlayerState OpponentPlayer => _players[1 - _activeIndex];
        public Faction FirstFaction => _players[_firstIndex].faction;

        // ── Setup ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Inicializa la partida. Coinflip determina quién juega primero. Tras esto,
        /// la fase queda en <see cref="GamePhase.AwaitingTurnStart"/>: la presentación
        /// debe llamar <see cref="BeginTurn"/> para arrancar el primer turno.
        /// </summary>
        public void StartGame(Faction player0Faction, Faction player1Faction)
        {
            _players[0] = NewPlayer(player0Faction);
            _players[1] = NewPlayer(player1Faction);

            _firstIndex = _rng.Next(2);
            _activeIndex = _firstIndex;
            HalfTurn = 0;
            _outcome = null;
            Phase = GamePhase.AwaitingTurnStart;
        }

        private PlayerState NewPlayer(Faction faction)
        {
            var p = new PlayerState
            {
                faction = faction,
                hp = _config.hpInitial,
                dinero = _config.initialDinero,
                fuerza = _config.initialFuerza,
                social = _config.initialSocial
            };
            var pool = _catalog.GetPool(faction);
            for (int i = 0; i < _config.handSize; i++)
                p.hand.Add(_rng.Choice(pool));
            return p;
        }

        // ── Fases 1 y 2: EFECTOS + PRODUCCIÓN ─────────────────────────────────────

        /// <summary>
        /// Arranca el turno del jugador activo: procesa sus statuses, aplica producción
        /// y daño de unidades enemigas, y evalúa victoria. Si la partida sigue, la fase
        /// pasa a <see cref="GamePhase.AwaitingAction"/>.
        /// </summary>
        public void BeginTurn()
        {
            if (Phase != GamePhase.AwaitingTurnStart)
                throw new InvalidOperationException($"BeginTurn requiere AwaitingTurnStart, está en {Phase}.");

            HalfTurn++;
            PlayerState active = ActivePlayer;
            PlayerState opp = OpponentPlayer;

            // ── EFECTOS: decrementar contadores; al llegar a 0, disparar y eliminar ──
            bool skipProduction = false;
            int productionMultiplier = 1;
            for (int i = active.activeStatuses.Count - 1; i >= 0; i--)
            {
                StatusEffect status = active.activeStatuses[i];
                status.counter--;
                if (status.counter == 0)
                {
                    if (status.statusType == StatusType.SkipProduction)
                        skipProduction = true;
                    else if (status.statusType == StatusType.DoubleProduction)
                        productionMultiplier = status.value;
                    active.activeStatuses.RemoveAt(i);
                }
            }

            // ── PRODUCCIÓN ──────────────────────────────────────────────────────
            if (!skipProduction)
            {
                // a) base + unidades productoras, b) por el multiplicador
                foreach (ResourceType r in ResourceTypes)
                    active.AddResource(r, _config.BaseProduction(r) * productionMultiplier, _config.maxResource);

                var unitProd = NewResourceTally();
                active.AddUnitProduction(unitProd);
                foreach (ResourceType r in ResourceTypes)
                    if (unitProd[r] != 0)
                        active.AddResource(r, unitProd[r] * productionMultiplier, _config.maxResource);

                // c) daño neto de unidades enemigas (defensas absorben primero)
                int net = Math.Max(0, opp.UnitAttack() - active.UnitDefense());
                active.hp = Math.Max(0, active.hp - net);
            }

            CheckVictory(active, opp);
            if (IsFinished) return;

            Phase = GamePhase.AwaitingAction;
        }

        // ── Fase 3: ACCIÓN ────────────────────────────────────────────────────────

        /// <summary>
        /// Juega la carta <paramref name="handIndex"/> de la mano del jugador activo.
        /// Valida todas las precondiciones ANTES de cobrar el costo (no se paga si la
        /// jugada requiere una decisión que falta). Tras resolver, repone la mano y
        /// ejecuta el fin de turno.
        /// </summary>
        /// <param name="unitSlotToReplace">Slot propio a reemplazar al desplegar una unidad con los slots llenos.</param>
        /// <param name="removeTargetSlot">Slot enemigo objetivo para efectos RemoveUnit.</param>
        public ActionResult PlayCard(int handIndex, int unitSlotToReplace = -1, int removeTargetSlot = -1)
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;

            PlayerState active = ActivePlayer;
            PlayerState opp = OpponentPlayer;

            if (handIndex < 0 || handIndex >= active.hand.Count) return ActionResult.IndexOutOfRange;
            CardData card = active.hand[handIndex];
            if (!active.CanAfford(card)) return ActionResult.CannotAfford;

            // Pre-validación de decisiones requeridas (antes de cobrar)
            if (card.cardType == CardType.Unidad)
            {
                bool hasSlot = active.SlotFor(card.id) != null;
                bool slotsFull = active.unitSlots.Count >= _config.maxSlots;
                if (!hasSlot && slotsFull &&
                    (unitSlotToReplace < 0 || unitSlotToReplace >= active.unitSlots.Count))
                    return ActionResult.NeedsUnitSlotChoice;
            }
            else if (CardHasRemoveUnit(card) && opp.unitSlots.Count > 0 &&
                     (removeTargetSlot < 0 || removeTargetSlot >= opp.unitSlots.Count))
            {
                return ActionResult.NeedsRemoveTarget;
            }

            // ── Commit ──────────────────────────────────────────────────────────
            active.Pay(card);

            if (card.cardType == CardType.Accion)
            {
                foreach (CardEffect effect in card.effects)
                    ResolveEffect(effect, active, opp, removeTargetSlot);

                CheckVictory(active, opp);
                if (IsFinished) return ActionResult.Success;  // partida terminada: sin reponer ni fin de turno
            }
            else
            {
                DeployUnit(card, active, unitSlotToReplace);
            }

            ReplaceCard(active, handIndex);
            EndOfTurn();
            return ActionResult.Success;
        }

        /// <summary>Descarta una carta: sin costo, sin efecto. Repone la mano y ejecuta el fin de turno.</summary>
        public ActionResult DiscardCard(int handIndex)
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;

            PlayerState active = ActivePlayer;
            if (handIndex < 0 || handIndex >= active.hand.Count) return ActionResult.IndexOutOfRange;

            ReplaceCard(active, handIndex);
            EndOfTurn();
            return ActionResult.Success;
        }

        // ── Fase 4: FIN DE TURNO ──────────────────────────────────────────────────

        private void EndOfTurn()
        {
            // Muerte súbita: daño incremental a ambos, ignora defensas (spec §5.1)
            if (HalfTurn >= _config.suddenDeathStart)
            {
                int suddenDeathDamage = HalfTurn - _config.suddenDeathStart + 1;
                _players[0].hp = Math.Max(0, _players[0].hp - suddenDeathDamage);
                _players[1].hp = Math.Max(0, _players[1].hp - suddenDeathDamage);

                CheckVictory(ActivePlayer, OpponentPlayer);
                if (IsFinished) return;
            }

            // Backstop duro: desempate determinista si se alcanza el límite de turnos
            if (HalfTurn >= _config.maxTurns)
            {
                TimeoutTiebreak();
                return;
            }

            _activeIndex = 1 - _activeIndex;
            Phase = GamePhase.AwaitingTurnStart;
        }

        // ── Resolución de efectos ─────────────────────────────────────────────────

        private void ResolveEffect(CardEffect effect, PlayerState active, PlayerState opp, int removeTargetSlot)
        {
            PlayerState tgt = effect.target == TargetType.Self ? active : opp;

            switch (effect.effectType)
            {
                case CardEffectType.ModifyHP:
                    tgt.hp = Math.Max(0, tgt.hp + effect.value);
                    break;

                case CardEffectType.ModifyResource:
                    tgt.AddResource(effect.resourceTarget, effect.value, _config.maxResource);
                    break;

                case CardEffectType.RemoveUnit:
                    PlayerState unitOwner = effect.target == TargetType.Opponent ? opp : active;
                    if (removeTargetSlot >= 0 && removeTargetSlot < unitOwner.unitSlots.Count)
                    {
                        UnitSlot slot = unitOwner.unitSlots[removeTargetSlot];
                        slot.count += effect.value;  // value = -1
                        if (slot.count <= 0)
                            unitOwner.unitSlots.RemoveAt(removeTargetSlot);
                    }
                    break;

                case CardEffectType.ApplyStatus:
                    tgt.activeStatuses.Add(effect.status.Clone());
                    break;
            }
        }

        private void DeployUnit(CardData card, PlayerState player, int slotToReplace)
        {
            UnitSlot existing = player.SlotFor(card.id);
            if (existing != null)
            {
                existing.count = Math.Min(existing.count + 1, _config.maxStack);
            }
            else if (player.unitSlots.Count < _config.maxSlots)
            {
                player.unitSlots.Add(new UnitSlot(card, 1));
            }
            else
            {
                // Slots llenos: reemplazar el slot elegido por la nueva unidad en x1
                player.unitSlots[slotToReplace] = new UnitSlot(card, 1);
            }
        }

        private void ReplaceCard(PlayerState player, int handIndex)
        {
            player.hand[handIndex] = _rng.Choice(_catalog.GetPool(player.faction));
        }

        // ── Victoria / desempate ──────────────────────────────────────────────────

        /// <summary>
        /// Evalúa victoria con prioridad KO &gt; Hegemonía Social &gt; Poder Económico (spec §5).
        /// El jugador activo (que acaba de actuar) tiene prioridad en empates de recurso.
        /// Si hay ganador, fija <see cref="Outcome"/> y la fase pasa a Finished.
        /// </summary>
        private void CheckVictory(PlayerState active, PlayerState opp)
        {
            if (opp.hp <= 0) { SetOutcome(active.faction, WinCondition.KO); return; }
            if (active.hp <= 0) { SetOutcome(opp.faction, WinCondition.KO); return; }

            if (active.social >= _config.socialThreshold) { SetOutcome(active.faction, WinCondition.HegemoniaSocial); return; }
            if (opp.social >= _config.socialThreshold) { SetOutcome(opp.faction, WinCondition.HegemoniaSocial); return; }

            if (active.dinero >= _config.dineroThreshold) { SetOutcome(active.faction, WinCondition.PoderEconomico); return; }
            if (opp.dinero >= _config.dineroThreshold) { SetOutcome(opp.faction, WinCondition.PoderEconomico); return; }
        }

        private void TimeoutTiebreak()
        {
            PlayerState p0 = _players[0];
            PlayerState p1 = _players[1];

            int winner;
            if (p0.hp != p1.hp)
            {
                winner = p0.hp > p1.hp ? 0 : 1;
            }
            else
            {
                int res0 = p0.dinero + p0.fuerza + p0.social;
                int res1 = p1.dinero + p1.fuerza + p1.social;
                if (res0 != res1)
                    winner = res0 > res1 ? 0 : 1;
                else
                    winner = 1 - _firstIndex;  // compensa la ventaja de iniciativa
            }

            SetOutcome(_players[winner].faction, WinCondition.Timeout);
        }

        private void SetOutcome(Faction winner, WinCondition condition)
        {
            _outcome = new GameOutcome(winner, condition, HalfTurn);
            Phase = GamePhase.Finished;
        }

        // ── Consultas para la presentación ────────────────────────────────────────

        public bool CanAfford(int handIndex)
        {
            if (Phase != GamePhase.AwaitingAction) return false;
            if (handIndex < 0 || handIndex >= ActivePlayer.hand.Count) return false;
            return ActivePlayer.CanAfford(ActivePlayer.hand[handIndex]);
        }

        /// <summary>True si jugar esta unidad exige elegir qué slot reemplazar (slots llenos).</summary>
        public bool RequiresUnitSlotChoice(int handIndex)
        {
            if (handIndex < 0 || handIndex >= ActivePlayer.hand.Count) return false;
            CardData card = ActivePlayer.hand[handIndex];
            if (card.cardType != CardType.Unidad) return false;
            return ActivePlayer.SlotFor(card.id) == null && ActivePlayer.unitSlots.Count >= _config.maxSlots;
        }

        /// <summary>True si la carta tiene un efecto RemoveUnit y el oponente tiene unidades que elegir.</summary>
        public bool RequiresRemoveTarget(int handIndex)
        {
            if (handIndex < 0 || handIndex >= ActivePlayer.hand.Count) return false;
            CardData card = ActivePlayer.hand[handIndex];
            return CardHasRemoveUnit(card) && OpponentPlayer.unitSlots.Count > 0;
        }

        private static bool CardHasRemoveUnit(CardData card)
        {
            foreach (CardEffect e in card.effects)
                if (e.effectType == CardEffectType.RemoveUnit)
                    return true;
            return false;
        }

        // ── Utilidades internas ─────────────────────────────────────────────────

        private static readonly ResourceType[] ResourceTypes =
            { ResourceType.Dinero, ResourceType.Fuerza, ResourceType.Social };

        private static Dictionary<ResourceType, int> NewResourceTally() =>
            new Dictionary<ResourceType, int>
            {
                { ResourceType.Dinero, 0 },
                { ResourceType.Fuerza, 0 },
                { ResourceType.Social, 0 }
            };
    }
}

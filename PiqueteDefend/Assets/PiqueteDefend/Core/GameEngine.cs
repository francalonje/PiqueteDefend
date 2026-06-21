using System;
using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Motor de juego determinista (spec §6, §7.8). Único responsable de resolver
    /// <see cref="CardEffect"/>, procesar <see cref="StatusEffect"/> (de jugador y por unidad),
    /// resolver ataques/curaciones/pasivas, aplicar muerte súbita y manejar transiciones de turno.
    /// C# puro: sin MonoBehaviours ni dependencias de escena. La semántica está validada contra el
    /// simulador de balance (`sim/`).
    ///
    /// Máquina de estados: la presentación llama <see cref="BeginTurn"/>, luego (en cualquier orden
    /// y opcionalmente) <see cref="PlayCard"/>/<see cref="DiscardCard"/> y <see cref="AttackWithUnit"/>,
    /// y finalmente <see cref="EndTurn"/>.
    ///
    /// Convención de turnos: <see cref="HalfTurn"/> cuenta turnos individuales (uno por jugador).
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

        private bool _cardActionUsed;
        private bool _attackUsed;

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

        public bool CardActionUsed => _cardActionUsed;
        public bool AttackUsed => _attackUsed;

        public PlayerState PlayerAt(int index) => _players[index];
        public PlayerState ActivePlayer => _players[_activeIndex];
        public PlayerState OpponentPlayer => _players[1 - _activeIndex];
        public Faction FirstFaction => _players[_firstIndex].faction;

        /// <summary>True si el jugador activo puede atacar este turno (regla de iniciativa, spec §3/§16).</summary>
        public bool CanAttackThisTurn => !(_config.firstNoAttackTurn1 && HalfTurn == 1);

        // ── Setup ───────────────────────────────────────────────────────────────

        public void StartGame(Faction player0Faction, Faction player1Faction, int firstIndex = -1)
        {
            _players[0] = NewPlayer(player0Faction);
            _players[1] = NewPlayer(player1Faction);

            _firstIndex = firstIndex >= 0 ? firstIndex : _rng.Next(2);
            _activeIndex = _firstIndex;
            HalfTurn = 0;
            _outcome = null;
            Phase = GamePhase.AwaitingTurnStart;
        }

        private PlayerState NewPlayer(Faction faction)
        {
            var p = new PlayerState(_config.maxSlots)
            {
                faction = faction,
                dinero = _config.initialDinero,
                fuerza = _config.initialFuerza,
                social = _config.initialSocial
            };

            foreach (UnitCardData unit in _catalog.GetStartingUnits(faction))
            {
                int slot = p.FirstFreeAllowedSlot(unit);
                if (slot >= 0) p.unitSlots[slot] = new UnitSlot(unit);
            }

            var pool = _catalog.GetPool(faction);
            for (int i = 0; i < _config.handSize; i++)
                p.hand.Add(WeightedDraw(pool));

            return p;
        }

        // ── Fases 1 y 2: EFECTOS + PRODUCCIÓN ─────────────────────────────────────

        public void BeginTurn()
        {
            if (Phase != GamePhase.AwaitingTurnStart)
                throw new InvalidOperationException($"BeginTurn requiere AwaitingTurnStart, está en {Phase}.");

            HalfTurn++;
            PlayerState active = ActivePlayer;

            // EFECTOS a) estados de JUGADOR (producción): fire-on-expiry.
            bool skipProduction = false;
            int productionMultiplier = 1;
            for (int i = active.activeStatuses.Count - 1; i >= 0; i--)
            {
                StatusEffect status = active.activeStatuses[i];
                status.counter--;
                if (status.counter <= 0)
                {
                    if (status.statusType == StatusType.SkipProduction) skipProduction = true;
                    else if (status.statusType == StatusType.DoubleProduction) productionMultiplier = status.value;
                    active.activeStatuses.RemoveAt(i);
                }
            }

            // EFECTOS b) estados por UNIDAD: Poison hace daño AHORA (el counter decrementa en FIN DE TURNO).
            for (int i = 0; i < active.unitSlots.Length; i++)
            {
                UnitSlot s = active.unitSlots[i];
                if (s == null) continue;
                int poison = s.StatusValue(StatusType.Poison);
                if (poison > 0) DirectDamage(active, i, poison);
            }
            CheckVictory();
            if (IsFinished) return;

            // EFECTOS c) pasivas de inicio de turno: Regeneration, TurnDamage, TurnStatus.
            ResolveTurnStartPassives(active, OpponentPlayer);
            CheckVictory();
            if (IsFinished) return;

            // PRODUCCIÓN: turno 1 sólo si firstProducesTurn1 (spec §3/§16); nunca si skipProduction.
            bool produces = HalfTurn > 1 || _config.firstProducesTurn1;
            if (produces && !skipProduction)
            {
                foreach (ResourceType r in ResourceTypes)
                {
                    int baseProd = _config.BaseProduction(r) * productionMultiplier;
                    if (baseProd != 0) active.AddResource(r, baseProd, _config.maxResource);
                }

                var unitProd = NewResourceTally();
                active.AddUnitProduction(unitProd);
                foreach (ResourceType r in ResourceTypes)
                    if (unitProd[r] != 0)
                        active.AddResource(r, unitProd[r] * productionMultiplier, _config.maxResource);
            }

            _cardActionUsed = false;
            _attackUsed = false;

            CheckVictory();
            if (IsFinished) return;

            Phase = GamePhase.AwaitingAction;
        }

        private void ResolveTurnStartPassives(PlayerState owner, PlayerState opp)
        {
            for (int srcIdx = 0; srcIdx < owner.unitSlots.Length; srcIdx++)
            {
                UnitSlot s = owner.unitSlots[srcIdx];
                if (s == null) continue;

                foreach (PassiveEffect pe in s.AllPassives())
                {
                    switch (pe.passiveType)
                    {
                        case PassiveType.Regeneration:
                        {
                            PlayerState board = pe.target == PassiveTarget.Enemies ? opp : owner;
                            foreach (int t in PassiveTargets(pe, srcIdx, board))
                            {
                                UnitSlot u = board.unitSlots[t];
                                if (u != null && u.currentHp < u.MaxHp)
                                    u.currentHp = Math.Min(u.MaxHp, u.currentHp + pe.value);
                            }
                            break;
                        }
                        case PassiveType.TurnDamage:
                        {
                            PlayerState board = pe.target == PassiveTarget.Enemies ? opp : owner;
                            foreach (int t in PassiveTargets(pe, srcIdx, board))
                                if (board.unitSlots[t] != null) DirectDamage(board, t, pe.value);
                            break;
                        }
                        case PassiveType.TurnStatus:
                        {
                            if (pe.status == null) break;
                            PlayerState board = pe.target == PassiveTarget.Enemies ? opp : owner;
                            foreach (int t in PassiveTargets(pe, srcIdx, board))
                                if (board.unitSlots[t] != null)
                                    board.unitSlots[t].activeStatuses.Add(pe.status.Clone());
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Slots objetivo de una pasiva dirigida (Self = sí misma; resto = patrón sobre <paramref name="board"/>).
        /// Honra <see cref="PassiveEffect.pickCount"/>: si es &gt; 0, elige N slots OCUPADOS del patrón
        /// (orden ascendente de índice, determinista — espejo en el sim).
        /// </summary>
        private List<int> PassiveTargets(PassiveEffect pe, int srcIdx, PlayerState board)
        {
            if (pe.target == PassiveTarget.Self) return new List<int> { srcIdx };

            List<int> candidates = ResolveSlots(pe.reference, pe.pattern, srcIdx);
            if (pe.pickCount <= 0) return candidates;

            var occupied = new List<int>();
            foreach (int t in candidates)
                if (board.unitSlots[t] != null) occupied.Add(t);
            occupied.Sort();
            if (occupied.Count > pe.pickCount) occupied.RemoveRange(pe.pickCount, occupied.Count - pe.pickCount);
            return occupied;
        }

        // ── Fase 3: ACCIÓN ────────────────────────────────────────────────────────

        public ActionResult PlayCard(int handIndex, int deploySlot = -1, int effectTargetSlot = -1,
                                     int effectTargetSlotB = -1)
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;
            if (_cardActionUsed) return ActionResult.AlreadyPlayedCard;

            PlayerState active = ActivePlayer;
            PlayerState opp = OpponentPlayer;

            if (handIndex < 0 || handIndex >= active.hand.Count) return ActionResult.IndexOutOfRange;
            CardData card = active.hand[handIndex];
            if (!active.CanAfford(card)) return ActionResult.CannotAfford;

            if (card is UnitCardData unit)
            {
                int slot = ResolveDeploySlot(unit, active, deploySlot, out ActionResult deployResult);
                if (deployResult != ActionResult.Success) return deployResult;

                active.Pay(card);
                active.unitSlots[slot] = new UnitSlot(unit);
            }
            else if (card is EquipmentCardData equip)
            {
                if (!active.HasAnyUnit()) return ActionResult.InvalidTarget;  // sin portador, no se puede jugar
                if (effectTargetSlot < 0) return ActionResult.NeedsEffectTarget;
                if (effectTargetSlot >= active.unitSlots.Length || active.unitSlots[effectTargetSlot] == null)
                    return ActionResult.InvalidTarget;

                active.Pay(card);
                active.unitSlots[effectTargetSlot].Attach(equip);
            }
            else if (card is ActionCardData action)
            {
                ActionResult pre = ValidateEffectTargets(action, active, opp, effectTargetSlot, effectTargetSlotB);
                if (pre != ActionResult.Success) return pre;

                active.Pay(card);
                foreach (CardEffect effect in action.effects)
                    ResolveEffect(effect, active, opp, effectTargetSlot, effectTargetSlotB);
            }

            ReplaceCard(active, handIndex);
            _cardActionUsed = true;
            CheckVictory();
            return ActionResult.Success;
        }

        public ActionResult DiscardCard(int handIndex)
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;
            if (_cardActionUsed) return ActionResult.AlreadyPlayedCard;

            PlayerState active = ActivePlayer;
            if (handIndex < 0 || handIndex >= active.hand.Count) return ActionResult.IndexOutOfRange;

            ReplaceCard(active, handIndex);
            _cardActionUsed = true;
            return ActionResult.Success;
        }

        public ActionResult AttackWithUnit(int attackerSlot, int[] chosenTargets = null)
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;
            if (_attackUsed) return ActionResult.AlreadyAttacked;
            if (!CanAttackThisTurn) return ActionResult.CannotAttackFirstTurn;

            PlayerState active = ActivePlayer;
            PlayerState opp = OpponentPlayer;

            if (attackerSlot < 0 || attackerSlot >= active.unitSlots.Length) return ActionResult.IndexOutOfRange;
            UnitSlot attacker = active.unitSlots[attackerSlot];
            if (attacker == null) return ActionResult.NoUnitInSlot;
            if (attacker.IsStunned) return ActionResult.UnitStunned;

            UnitAttack ua = attacker.unit.attack;
            List<int> candidates = ResolveSlots(ua.reference, ua.pattern, attackerSlot);

            int[] targets;
            if (!ua.RequiresChoice)
            {
                targets = candidates.ToArray();
            }
            else
            {
                if (chosenTargets == null || chosenTargets.Length != ua.pickCount)
                    return ActionResult.NeedsAttackTarget;
                foreach (int t in chosenTargets)
                    if (!candidates.Contains(t)) return ActionResult.InvalidTarget;
                targets = chosenTargets;
            }

            // Regla de objetivos (spec §6): la acción se CANCELA (sin gastar el ataque) si no afecta a
            // ningún objetivo válido. Si afecta al menos a uno, se permite aunque otros golpes whiffeen.
            if (!HasValidTarget(ua, targets, active, opp)) return ActionResult.InvalidTarget;

            if (ua.IsHeal)
            {
                foreach (int t in targets)
                {
                    if (t < 0 || t >= active.unitSlots.Length) continue;
                    UnitSlot u = active.unitSlots[t];
                    if (u != null && u.currentHp < u.MaxHp)
                        u.currentHp = Math.Min(u.MaxHp, u.currentHp + ua.damagePerSlot);  // whiff si vacío/llena
                }
                _attackUsed = true;
                return ActionResult.Success;
            }

            // Daño efectivo (base + equipo + Furia + Aura − Desmoralizar) + Retaliate de los defensores.
            int dmg = EffectiveAttackDamage(active.unitSlots, attackerSlot);
            int retaliation = 0;
            foreach (int t in targets)
            {
                if (t < 0 || t >= opp.unitSlots.Length) continue;
                UnitSlot def = opp.unitSlots[t];
                if (def == null) continue;  // whiff (spec §6)
                foreach (PassiveEffect pe in def.AllPassives())
                    if (pe.passiveType == PassiveType.Retaliate) retaliation += pe.value;
                def.currentHp -= dmg;
                if (def.IsDead) opp.unitSlots[t] = null;
            }
            if (retaliation > 0 && active.unitSlots[attackerSlot] != null)
            {
                attacker.currentHp -= retaliation;
                if (attacker.IsDead) active.unitSlots[attackerSlot] = null;
            }

            _attackUsed = true;
            CheckVictory();
            return ActionResult.Success;
        }

        public ActionResult EndTurn()
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;
            EndOfTurn();
            return ActionResult.Success;
        }

        // ── Fase 5: FIN DE TURNO ──────────────────────────────────────────────────

        private void EndOfTurn()
        {
            // a) decrementar estados por unidad del jugador activo (active-while-present, spec §7.7).
            TickUnitStatuses(ActivePlayer);

            // b) muerte súbita (spec §5.1): daño a todas las unidades de ambos, ignora defensas.
            if (HalfTurn >= _config.suddenDeathStart)
            {
                ApplySuddenDeath();
                CheckVictory();
                if (IsFinished) return;
            }

            if (HalfTurn >= _config.maxTurns)
            {
                TimeoutTiebreak();
                return;
            }

            _activeIndex = 1 - _activeIndex;
            Phase = GamePhase.AwaitingTurnStart;
        }

        private void TickUnitStatuses(PlayerState p)
        {
            foreach (UnitSlot s in p.unitSlots)
            {
                if (s == null) continue;
                for (int i = s.activeStatuses.Count - 1; i >= 0; i--)
                {
                    s.activeStatuses[i].counter--;
                    if (s.activeStatuses[i].counter <= 0) s.activeStatuses.RemoveAt(i);
                }
            }
        }

        private void ApplySuddenDeath()
        {
            int dmg = _config.suddenDeathDamage;
            foreach (PlayerState p in _players)
                for (int i = 0; i < p.unitSlots.Length; i++)
                {
                    UnitSlot s = p.unitSlots[i];
                    if (s == null) continue;
                    s.currentHp -= dmg;
                    if (s.IsDead) p.unitSlots[i] = null;
                }
        }

        // ── Resolución de efectos / despliegue ────────────────────────────────────

        private int ResolveDeploySlot(UnitCardData unit, PlayerState player, int requested, out ActionResult result)
        {
            result = ActionResult.Success;

            // No hay reemplazo: una unidad sólo se despliega en un slot permitido y LIBRE (spec §8.3).
            if (requested >= 0)
            {
                if (requested >= player.unitSlots.Length || !unit.AllowsSlot(requested)
                    || player.unitSlots[requested] != null)
                {
                    result = ActionResult.InvalidTarget;
                    return -1;
                }
                return requested;
            }

            int free = player.FirstFreeAllowedSlot(unit);
            if (free >= 0) return free;

            // Sin slot permitido libre, la unidad no se puede jugar (la carta queda en la mano).
            result = ActionResult.InvalidTarget;
            return -1;
        }

        private ActionResult ValidateEffectTargets(ActionCardData action, PlayerState active, PlayerState opp,
                                                   int chosen, int chosenB)
        {
            foreach (CardEffect e in action.effects)
            {
                if (!e.TargetsAUnitSlot) continue;

                PlayerState owner = e.target == TargetType.Self ? active : opp;
                if (!owner.HasAnyUnit()) continue;  // sin objetivos → whiff, no requiere elección

                int slot = e.targetSlot >= 0 ? e.targetSlot : chosen;
                if (slot < 0 || slot >= owner.unitSlots.Length || owner.unitSlots[slot] == null)
                    return ActionResult.NeedsEffectTarget;

                if (e.NeedsSecondSlot)
                {
                    int slotB = e.targetSlotB >= 0 ? e.targetSlotB : chosenB;
                    if (slotB < 0 || slotB >= owner.unitSlots.Length) return ActionResult.NeedsSecondSlot;
                }
            }
            return ActionResult.Success;
        }

        private void ResolveEffect(CardEffect effect, PlayerState active, PlayerState opp, int chosen, int chosenB)
        {
            PlayerState tgt = effect.target == TargetType.Self ? active : opp;

            switch (effect.effectType)
            {
                case CardEffectType.ModifyResource:
                    tgt.AddResource(effect.resourceTarget, effect.value, _config.maxResource);
                    break;

                case CardEffectType.ModifyHP:
                {
                    int slot = effect.targetSlot >= 0 ? effect.targetSlot : chosen;
                    if (slot >= 0 && slot < tgt.unitSlots.Length && tgt.unitSlots[slot] != null)
                    {
                        UnitSlot u = tgt.unitSlots[slot];
                        u.currentHp += effect.value;
                        if (u.IsDead) tgt.unitSlots[slot] = null;
                        else if (u.currentHp > u.MaxHp) u.currentHp = u.MaxHp;
                    }
                    break;
                }

                case CardEffectType.RemoveUnit:
                {
                    int slot = effect.targetSlot >= 0 ? effect.targetSlot : chosen;
                    if (slot >= 0 && slot < tgt.unitSlots.Length) tgt.unitSlots[slot] = null;
                    break;
                }

                case CardEffectType.ApplyStatus:
                {
                    if (effect.status == null) break;
                    if (StatusEffect.IsPlayerStatus(effect.status.statusType))
                    {
                        tgt.activeStatuses.Add(effect.status.Clone());
                    }
                    else
                    {
                        int slot = effect.targetSlot >= 0 ? effect.targetSlot : chosen;
                        if (slot >= 0 && slot < tgt.unitSlots.Length && tgt.unitSlots[slot] != null)
                            tgt.unitSlots[slot].activeStatuses.Add(effect.status.Clone());
                    }
                    break;
                }

                case CardEffectType.MoveUnit:
                {
                    int src = effect.targetSlot >= 0 ? effect.targetSlot : chosen;
                    int dst = effect.targetSlotB >= 0 ? effect.targetSlotB : chosenB;
                    if (src >= 0 && src < tgt.unitSlots.Length && dst >= 0 && dst < tgt.unitSlots.Length
                        && tgt.unitSlots[src] != null && tgt.unitSlots[dst] == null
                        && tgt.unitSlots[src].unit.AllowsSlot(dst))
                    {
                        tgt.unitSlots[dst] = tgt.unitSlots[src];
                        tgt.unitSlots[src] = null;
                    }
                    break;
                }

                case CardEffectType.SwapUnits:
                {
                    int a = effect.targetSlot >= 0 ? effect.targetSlot : chosen;
                    int b = effect.targetSlotB >= 0 ? effect.targetSlotB : chosenB;
                    if (a >= 0 && a < tgt.unitSlots.Length && b >= 0 && b < tgt.unitSlots.Length && a != b)
                    {
                        UnitSlot tmp = tgt.unitSlots[a];
                        tgt.unitSlots[a] = tgt.unitSlots[b];
                        tgt.unitSlots[b] = tmp;
                    }
                    break;
                }
            }
        }

        // ── Cálculo de combate ────────────────────────────────────────────────────

        /// <summary>Resuelve los slots objetivo (0–5) de un patrón. Absolute = slots fijos; Relative = offsets desde origin.</summary>
        public static List<int> ResolveSlots(AttackReference reference, int[] pattern, int origin)
        {
            var list = new List<int>();
            if (pattern == null) return list;
            foreach (int p in pattern)
            {
                int idx = reference == AttackReference.Absolute ? p : origin + p;
                if (idx >= 0 && idx < BoardSize && !list.Contains(idx)) list.Add(idx);
            }
            return list;
        }

        /// <summary>Suma de AuraDamage de aliadas cuyo patrón cubre al atacante en <paramref name="slotIndex"/>.</summary>
        public int AuraBonusFor(UnitSlot[] board, int slotIndex)
        {
            int total = 0;
            for (int srcIdx = 0; srcIdx < board.Length; srcIdx++)
            {
                UnitSlot src = board[srcIdx];
                if (src == null || srcIdx == slotIndex) continue;
                foreach (PassiveEffect pe in src.AllPassives())
                    if (pe.passiveType == PassiveType.AuraDamage && pe.target == PassiveTarget.Allies
                        && ResolveSlots(pe.reference, pe.pattern, srcIdx).Contains(slotIndex))
                        total += pe.value;
            }
            return total;
        }

        /// <summary>Daño efectivo de la unidad en <paramref name="slotIndex"/>: base + equipo + Furia + Aura − Desmoralizar (≥0).</summary>
        public int EffectiveAttackDamage(UnitSlot[] board, int slotIndex)
        {
            UnitSlot s = board[slotIndex];
            int dmg = s.unit.attack.damagePerSlot + s.EquipmentDamage;
            dmg += s.StatusValue(StatusType.Furia) - s.StatusValue(StatusType.Desmoralizar);
            dmg += AuraBonusFor(board, slotIndex);
            return dmg < 0 ? 0 : dmg;
        }

        /// <summary>
        /// True si el ataque afecta al menos a un objetivo válido: para daño, un slot enemigo ocupado;
        /// para cura, un aliado por debajo de su maxHp. Si no, la acción se cancela (spec §6).
        /// </summary>
        public static bool HasValidTarget(UnitAttack ua, int[] targets, PlayerState active, PlayerState opp)
        {
            foreach (int t in targets)
            {
                if (ua.effect == AttackEffect.HealAllies)
                {
                    if (t < 0 || t >= active.unitSlots.Length) continue;
                    UnitSlot u = active.unitSlots[t];
                    if (u != null && u.currentHp < u.MaxHp) return true;
                }
                else
                {
                    if (t < 0 || t >= opp.unitSlots.Length) continue;
                    if (opp.unitSlots[t] != null) return true;
                }
            }
            return false;
        }

        private void DirectDamage(PlayerState owner, int slot, int amount)
        {
            UnitSlot u = owner.unitSlots[slot];
            if (u == null) return;
            u.currentHp -= amount;
            if (u.IsDead) owner.unitSlots[slot] = null;
        }

        // ── Robo ponderado (spec §8.1) ────────────────────────────────────────────

        private CardData WeightedDraw(IReadOnlyList<CardData> pool)
        {
            int total = 0;
            foreach (CardData c in pool) total += c.drawWeight > 0 ? c.drawWeight : 1;
            if (total <= 0) return _rng.Choice(pool);

            int r = _rng.Next(total);
            foreach (CardData c in pool)
            {
                int w = c.drawWeight > 0 ? c.drawWeight : 1;
                if (r < w) return c;
                r -= w;
            }
            return pool[pool.Count - 1];
        }

        private void ReplaceCard(PlayerState player, int handIndex)
        {
            player.hand[handIndex] = WeightedDraw(_catalog.GetPool(player.faction));
        }

        // ── Victoria / desempate ──────────────────────────────────────────────────

        private void CheckVictory()
        {
            if (IsFinished) return;
            bool p0Alive = _players[0].HasAnyUnit();
            bool p1Alive = _players[1].HasAnyUnit();

            if (!p0Alive && !p1Alive) { SetDraw(); return; }
            if (!p1Alive) { SetOutcome(_players[0].faction, WinCondition.KO); return; }
            if (!p0Alive) { SetOutcome(_players[1].faction, WinCondition.KO); return; }
        }

        private void TimeoutTiebreak()
        {
            PlayerState p0 = _players[0];
            PlayerState p1 = _players[1];

            int a0 = p0.AliveUnitCount();
            int a1 = p1.AliveUnitCount();

            int winner;
            if (a0 != a1)
            {
                winner = a0 > a1 ? 0 : 1;
            }
            else
            {
                int hp0 = p0.TotalUnitHp();
                int hp1 = p1.TotalUnitHp();
                winner = hp0 != hp1 ? (hp0 > hp1 ? 0 : 1) : 1 - _firstIndex;
            }

            SetOutcome(_players[winner].faction, WinCondition.Timeout);
        }

        private void SetOutcome(Faction winner, WinCondition condition)
        {
            _outcome = GameOutcome.Win(winner, condition, HalfTurn);
            Phase = GamePhase.Finished;
        }

        private void SetDraw()
        {
            _outcome = GameOutcome.Draw(HalfTurn);
            Phase = GamePhase.Finished;
        }

        // ── Consultas para la presentación ────────────────────────────────────────

        public bool CanAfford(int handIndex)
        {
            if (Phase != GamePhase.AwaitingAction) return false;
            if (handIndex < 0 || handIndex >= ActivePlayer.hand.Count) return false;
            return ActivePlayer.CanAfford(ActivePlayer.hand[handIndex]);
        }

        /// <summary>
        /// Sin reemplazo, el despliegue nunca requiere que el jugador elija slot: si hay un slot
        /// permitido libre, el motor toma el primero (o el que indique el drag); si no, la carta no
        /// se puede jugar. Se mantiene por compatibilidad de la presentación (siempre false).
        /// </summary>
        public bool RequiresDeploySlot(int handIndex) => false;

        /// <summary>True si la carta necesita que el jugador elija una unidad objetivo (acción sobre unidad o equipo).</summary>
        public bool RequiresEffectTarget(int handIndex)
        {
            if (handIndex < 0 || handIndex >= ActivePlayer.hand.Count) return false;
            CardData card = ActivePlayer.hand[handIndex];

            if (card is EquipmentCardData) return ActivePlayer.HasAnyUnit();

            if (card is ActionCardData action)
            {
                foreach (CardEffect e in action.effects)
                {
                    if (!e.TargetsAUnitSlot || e.targetSlot >= 0) continue;
                    PlayerState owner = e.target == TargetType.Self ? ActivePlayer : OpponentPlayer;
                    if (owner.HasAnyUnit()) return true;
                }
            }
            return false;
        }

        public bool AttackRequiresTarget(int attackerSlot)
        {
            if (attackerSlot < 0 || attackerSlot >= ActivePlayer.unitSlots.Length) return false;
            UnitSlot s = ActivePlayer.unitSlots[attackerSlot];
            return s != null && s.unit.attack.RequiresChoice;
        }

        // ── Utilidades internas ─────────────────────────────────────────────────

        private const int BoardSize = 6;

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

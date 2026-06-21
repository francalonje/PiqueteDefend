using System;
using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Motor de juego determinista (spec §6, §7.8). Único responsable de resolver
    /// <see cref="CardEffect"/>, procesar <see cref="StatusEffect"/>, resolver ataques de
    /// unidades, aplicar muerte súbita y manejar las transiciones de turno. C# puro: sin
    /// MonoBehaviours ni dependencias de escena.
    ///
    /// Máquina de estados: expone fases (<see cref="GamePhase"/>) y espera que la presentación
    /// llame <see cref="BeginTurn"/>, luego (en cualquier orden y opcionalmente) <see cref="PlayCard"/>
    /// o <see cref="DiscardCard"/> y <see cref="AttackWithUnit"/>, y finalmente <see cref="EndTurn"/>.
    ///
    /// Convención de turnos: <see cref="HalfTurn"/> cuenta turnos individuales (uno por jugador).
    /// <c>suddenDeathStart</c> y <c>maxTurns</c> se miden en esa unidad.
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

        // Acciones consumidas en el turno activo (una carta + un ataque, spec §6).
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

        // ── Setup ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Inicializa la partida: recursos iniciales, unidades iniciales desplegadas y manos.
        /// Queda en <see cref="GamePhase.AwaitingTurnStart"/>.
        /// </summary>
        /// <param name="firstIndex">
        /// Índice (0/1) del jugador que arranca. Si es negativo (default), se decide por coinflip.
        /// </param>
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

            // Unidades iniciales (spec §6): se despliegan en su primer slot permitido libre.
            foreach (UnitCardData unit in _catalog.GetStartingUnits(faction))
            {
                int slot = p.FirstFreeAllowedSlot(unit);
                if (slot >= 0) p.unitSlots[slot] = new UnitSlot(unit);
            }

            var pool = _catalog.GetPool(faction);
            for (int i = 0; i < _config.handSize; i++)
                p.hand.Add(_rng.Choice(pool));

            return p;
        }

        // ── Fases 1 y 2: EFECTOS + PRODUCCIÓN ─────────────────────────────────────

        /// <summary>
        /// Arranca el turno del jugador activo: procesa sus statuses, aplica producción y evalúa
        /// victoria. Si la partida sigue, la fase pasa a <see cref="GamePhase.AwaitingAction"/>.
        /// </summary>
        public void BeginTurn()
        {
            if (Phase != GamePhase.AwaitingTurnStart)
                throw new InvalidOperationException($"BeginTurn requiere AwaitingTurnStart, está en {Phase}.");

            HalfTurn++;
            PlayerState active = ActivePlayer;

            // ── EFECTOS: decrementar contadores; al llegar a 0, activar y eliminar ──
            bool skipProduction = false;
            int productionMultiplier = 1;
            for (int i = active.activeStatuses.Count - 1; i >= 0; i--)
            {
                StatusEffect status = active.activeStatuses[i];
                status.counter--;
                if (status.counter <= 0)
                {
                    if (status.statusType == StatusType.SkipProduction)
                        skipProduction = true;
                    else if (status.statusType == StatusType.DoubleProduction)
                        productionMultiplier = status.value;
                    active.activeStatuses.RemoveAt(i);
                }
            }

            // ── PRODUCCIÓN: no en el turno 1 de la partida (spec §3/§6) ─────────────
            if (HalfTurn > 1 && !skipProduction)
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

        // ── Fase 3: ACCIÓN ────────────────────────────────────────────────────────

        /// <summary>
        /// Juega la carta <paramref name="handIndex"/>. Para unidades, <paramref name="deploySlot"/>
        /// indica dónde desplegar/reemplazar (-1 = primer slot permitido libre). Para acciones con
        /// efecto sobre una unidad, <paramref name="effectTargetSlot"/> es el slot elegido.
        /// Valida las decisiones requeridas ANTES de cobrar. No termina el turno.
        /// </summary>
        public ActionResult PlayCard(int handIndex, int deploySlot = -1, int effectTargetSlot = -1)
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
            else if (card is ActionCardData action)
            {
                ActionResult pre = ValidateEffectTargets(action, active, opp, effectTargetSlot);
                if (pre != ActionResult.Success) return pre;

                active.Pay(card);
                foreach (CardEffect effect in action.effects)
                    ResolveEffect(effect, active, opp, effectTargetSlot);
            }

            ReplaceCard(active, handIndex);
            _cardActionUsed = true;
            CheckVictory();
            return ActionResult.Success;
        }

        /// <summary>Descarta una carta: sin costo, sin efecto. Repone la mano. No termina el turno.</summary>
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

        /// <summary>
        /// Ataca con la unidad en <paramref name="attackerSlot"/>. Si su ataque es a elección
        /// (<see cref="UnitAttack.RequiresChoice"/>), <paramref name="chosenTargets"/> debe traer
        /// exactamente <c>pickCount</c> slots del patrón. Slots vacíos = whiff. No termina el turno.
        /// </summary>
        public ActionResult AttackWithUnit(int attackerSlot, int[] chosenTargets = null)
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;
            if (_attackUsed) return ActionResult.AlreadyAttacked;

            PlayerState active = ActivePlayer;
            PlayerState opp = OpponentPlayer;

            if (attackerSlot < 0 || attackerSlot >= active.unitSlots.Length) return ActionResult.IndexOutOfRange;
            UnitSlot attacker = active.unitSlots[attackerSlot];
            if (attacker == null) return ActionResult.NoUnitInSlot;

            UnitAttack ua = attacker.unit.attack;
            List<int> candidates = ResolveAttackCandidates(ua, attackerSlot);

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

            foreach (int t in targets)
            {
                if (t < 0 || t >= opp.unitSlots.Length) continue;
                UnitSlot def = opp.unitSlots[t];
                if (def == null) continue;  // whiff (spec §6)
                def.currentHp -= ua.damagePerSlot;
                if (def.IsDead) opp.unitSlots[t] = null;
            }

            _attackUsed = true;
            CheckVictory();
            return ActionResult.Success;
        }

        /// <summary>Termina el turno del jugador activo: muerte súbita, victoria y pase de turno.</summary>
        public ActionResult EndTurn()
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;
            EndOfTurn();
            return ActionResult.Success;
        }

        // ── Fase 5: FIN DE TURNO ──────────────────────────────────────────────────

        private void EndOfTurn()
        {
            // Muerte súbita: daño a todas las unidades de ambos, ignora defensas (spec §5.1).
            if (HalfTurn >= _config.suddenDeathStart)
            {
                ApplySuddenDeath();
                CheckVictory();
                if (IsFinished) return;
            }

            // Backstop duro: desempate determinista al alcanzar el límite de turnos.
            if (HalfTurn >= _config.maxTurns)
            {
                TimeoutTiebreak();
                return;
            }

            _activeIndex = 1 - _activeIndex;
            Phase = GamePhase.AwaitingTurnStart;
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

            if (requested >= 0)
            {
                if (requested >= player.unitSlots.Length || !unit.AllowsSlot(requested))
                {
                    result = ActionResult.InvalidTarget;
                    return -1;
                }
                return requested;  // libre u ocupado (reemplazo), ambos válidos si está permitido
            }

            int free = player.FirstFreeAllowedSlot(unit);
            if (free >= 0) return free;

            if (!player.HasAnyAllowedSlot(unit))
            {
                result = ActionResult.InvalidTarget;  // no debería pasar (allowedSlots vacío = cualquiera)
                return -1;
            }

            result = ActionResult.NeedsDeploySlot;     // slots permitidos llenos: elegir cuál reemplazar
            return -1;
        }

        private ActionResult ValidateEffectTargets(ActionCardData action, PlayerState active, PlayerState opp, int chosen)
        {
            foreach (CardEffect e in action.effects)
            {
                if (!e.TargetsAUnitSlot || e.targetSlot >= 0) continue;  // sin choice o slot fijo

                PlayerState owner = e.target == TargetType.Self ? active : opp;
                if (!owner.HasAnyUnit()) continue;  // sin objetivos → whiff, no requiere elección

                if (chosen < 0 || chosen >= owner.unitSlots.Length || owner.unitSlots[chosen] == null)
                    return ActionResult.NeedsEffectTarget;
            }
            return ActionResult.Success;
        }

        private void ResolveEffect(CardEffect effect, PlayerState active, PlayerState opp, int chosen)
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
                        else if (u.currentHp > u.MaxHp) u.currentHp = u.MaxHp;  // la cura no supera el máximo
                    }
                    break;
                }

                case CardEffectType.RemoveUnit:
                {
                    int slot = effect.targetSlot >= 0 ? effect.targetSlot : chosen;
                    if (slot >= 0 && slot < tgt.unitSlots.Length)
                        tgt.unitSlots[slot] = null;
                    break;
                }

                case CardEffectType.ApplyStatus:
                    tgt.activeStatuses.Add(effect.status.Clone());
                    break;
            }
        }

        private List<int> ResolveAttackCandidates(UnitAttack ua, int attackerSlot)
        {
            var list = new List<int>();
            int n = _config.maxSlots;
            foreach (int p in ua.pattern)
            {
                int idx = ua.reference == AttackReference.Absolute ? p : attackerSlot + p;
                if (idx >= 0 && idx < n && !list.Contains(idx)) list.Add(idx);
            }
            return list;
        }

        private void ReplaceCard(PlayerState player, int handIndex)
        {
            player.hand[handIndex] = _rng.Choice(_catalog.GetPool(player.faction));
        }

        // ── Victoria / desempate ──────────────────────────────────────────────────

        /// <summary>
        /// Evalúa la victoria por KO (spec §5). Si ambos pierden su última unidad a la vez → empate.
        /// </summary>
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
                if (hp0 != hp1)
                    winner = hp0 > hp1 ? 0 : 1;
                else
                    winner = 1 - _firstIndex;  // compensa la ventaja de iniciativa
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

        /// <summary>True si desplegar esta unidad exige elegir slot (todos los permitidos ocupados).</summary>
        public bool RequiresDeploySlot(int handIndex)
        {
            if (handIndex < 0 || handIndex >= ActivePlayer.hand.Count) return false;
            if (ActivePlayer.hand[handIndex] is not UnitCardData unit) return false;
            return ActivePlayer.FirstFreeAllowedSlot(unit) < 0 && ActivePlayer.HasAnyAllowedSlot(unit);
        }

        /// <summary>True si la carta tiene un efecto sobre unidad y el dueño objetivo tiene unidades que elegir.</summary>
        public bool RequiresEffectTarget(int handIndex)
        {
            if (handIndex < 0 || handIndex >= ActivePlayer.hand.Count) return false;
            if (ActivePlayer.hand[handIndex] is not ActionCardData action) return false;

            foreach (CardEffect e in action.effects)
            {
                if (!e.TargetsAUnitSlot || e.targetSlot >= 0) continue;
                PlayerState owner = e.target == TargetType.Self ? ActivePlayer : OpponentPlayer;
                if (owner.HasAnyUnit()) return true;
            }
            return false;
        }

        /// <summary>True si atacar con esa unidad requiere que el jugador elija slot(s) objetivo.</summary>
        public bool AttackRequiresTarget(int attackerSlot)
        {
            if (attackerSlot < 0 || attackerSlot >= ActivePlayer.unitSlots.Length) return false;
            UnitSlot s = ActivePlayer.unitSlots[attackerSlot];
            return s != null && s.unit.attack.RequiresChoice;
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

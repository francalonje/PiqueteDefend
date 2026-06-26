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

        /// <summary>True si el jugador activo puede atacar este turno (regla de iniciativa, spec §3/§16).</summary>
        public bool CanAttackThisTurn => !(_config.firstNoAttackTurn1 && HalfTurn == 1);

        /// <summary>True si la unidad del jugador activo en <paramref name="slot"/> puede atacar AHORA
        /// (ocupada, no aturdida, regla de iniciativa OK y sin haber atacado este turno, spec §6).
        /// Centraliza la condición "puede actuar" para la UI y los tests.</summary>
        public bool UnitCanAttack(int slot) => UnitCouldAttack(slot) && CanAffordAttack(slot);

        /// <summary>"Puede actuar" IGNORANDO el costo en ⚡: ocupada, no aturdida, sin atacar este turno y
        /// regla de iniciativa OK. La UI lo usa para permitir CLICKEAR la unidad y mostrar el popover
        /// (deshabilitado si no alcanza la Fuerza); el costo se chequea aparte con <see cref="CanAffordAttack"/>.</summary>
        public bool UnitCouldAttack(int slot)
        {
            if (!CanAttackThisTurn) return false;
            if (slot < 0 || slot >= ActivePlayer.unitSlots.Length) return false;
            UnitSlot s = ActivePlayer.unitSlots[slot];
            return s != null && !s.IsStunned && !s.attackedThisTurn;
        }

        /// <summary>⚡ Fuerza que cuesta el ataque de la unidad en <paramref name="slot"/> (0 si no hay unidad).</summary>
        public int AttackCost(int slot)
        {
            if (slot < 0 || slot >= ActivePlayer.unitSlots.Length) return 0;
            UnitSlot s = ActivePlayer.unitSlots[slot];
            return s == null ? 0 : AttackCostFor(s.unit.attack);
        }

        /// <summary>⚡ que costaría el ataque descrito por <paramref name="ua"/> (preview de carta de unidad).
        /// Proporcional al daño TOTAL por objetivo (daño por golpe × golpes): repartirlo en multi-hit no lo abarata.</summary>
        public int AttackCostFor(UnitAttack ua) =>
            ua == null ? 0 : _config.AttackFuerzaCost(ua.damagePerSlot * ua.EffectiveHits);

        /// <summary>True si el jugador activo puede pagar el ataque de la unidad en <paramref name="slot"/>.</summary>
        public bool CanAffordAttack(int slot) =>
            ActivePlayer.GetResource(ResourceType.Fuerza) >= AttackCost(slot);

        /// <summary>% de inflación vigente este medio-turno (spec §3). 0 = inflación no arrancó.</summary>
        public int InflationPercent
        {
            get
            {
                int start = _config.inflationStartTurn;
                if (start <= 0 || HalfTurn < start) return 0;
                return (HalfTurn - start + 1) * _config.inflationPercentPerTurn;
            }
        }

        /// <summary>True una vez que la inflación está activa (para que la UI muestre el medidor).</summary>
        public bool InflationActive => InflationPercent > 0;

        // ── Setup ───────────────────────────────────────────────────────────────

        /// <summary>Inicio 2-jugadores (spec §6): facciones puras, sin handicaps ni mazo inyectado.
        /// Conveniencia sobre <see cref="StartGame(PlayerSetup,PlayerSetup,int)"/>.</summary>
        public void StartGame(Faction player0Faction, Faction player1Faction, int firstIndex = -1)
            => StartGame(PlayerSetup.ForFaction(player0Faction), PlayerSetup.ForFaction(player1Faction), firstIndex);

        /// <summary>
        /// Inicio con <see cref="PlayerSetup"/> por jugador (spec §7.8/§17.5): permite inyectar el
        /// mazo de la run y handicaps de dificultad. Con setups <see cref="PlayerSetup.ForFaction"/>
        /// es idéntico al inicio 2-jugadores.
        /// </summary>
        public void StartGame(PlayerSetup player0, PlayerSetup player1, int firstIndex = -1)
        {
            _players[0] = NewPlayer(player0);
            _players[1] = NewPlayer(player1);

            _firstIndex = firstIndex >= 0 ? firstIndex : _rng.Next(2);
            _activeIndex = _firstIndex;
            HalfTurn = 0;
            _outcome = null;
            Phase = GamePhase.AwaitingTurnStart;
        }

        private PlayerState NewPlayer(PlayerSetup setup)
        {
            Faction faction = setup.faction;
            var p = new PlayerState(_config.maxSlots) { faction = faction };

            // Recursos iniciales + bonus de handicap, recortados al techo (spec §3/§17.1).
            p.SetResource(ResourceType.Dinero, _config.initialDinero + setup.bonusDinero, _config.maxResource);
            p.SetResource(ResourceType.Fuerza, _config.initialFuerza + setup.bonusFuerza, _config.maxResource);
            p.SetResource(ResourceType.Social, _config.initialSocial + setup.bonusSocial, _config.maxResource);

            // Unidades iniciales (base del setup o del catálogo) + extra de handicap (spec §17.1).
            DeployStarting(p, setup.startingUnits ?? _catalog.GetStartingUnits(faction));
            if (setup.extraStartingUnits != null) DeployStarting(p, setup.extraStartingUnits);

            // Mazo: el inyectado (run) o el del catálogo (spec §8.1/§17.2).
            p.deck.AddRange(setup.deck ?? _catalog.GetDeckList(faction));
            Shuffle(p.deck);
            for (int i = 0; i < _config.handSize; i++)
            {
                CardData drawn = DrawCard(p);
                if (drawn != null) p.hand.Add(drawn);
            }

            return p;
        }

        /// <summary>Despliega una tanda de unidades iniciales en sus slots de apertura (spec §11.3).</summary>
        private void DeployStarting(PlayerState p, IReadOnlyList<UnitCardData> units)
        {
            foreach (UnitCardData unit in units)
            {
                int slot = StartingSlot(p, unit);
                if (slot >= 0) p.unitSlots[slot] = new UnitSlot(unit);
            }
        }

        /// <summary>
        /// Posición de una unidad inicial (spec §11.3): un muro (restringido al frente) arranca
        /// <b>adelante de todo</b> — el slot libre permitido de mayor índice — para que nada se
        /// despliegue por delante y tankee siempre; el resto arranca en la retaguardia (menor
        /// índice), protegido detrás del muro.
        /// </summary>
        private int StartingSlot(PlayerState p, UnitCardData unit)
        {
            int front = _config.maxSlots / 2;
            bool frontLocked = unit.allowedSlots != null && unit.allowedSlots.Length > 0
                               && Array.TrueForAll(unit.allowedSlots, s => s >= front);
            if (frontLocked)
            {
                for (int i = p.unitSlots.Length - 1; i >= 0; i--)
                    if (p.unitSlots[i] == null && unit.AllowsSlot(i)) return i;
                return -1;
            }
            return p.FirstFreeAllowedSlot(unit);
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

            // Cada unidad del jugador activo puede atacar una vez este turno (spec §6).
            foreach (UnitSlot s in active.unitSlots)
                if (s != null) s.attackedThisTurn = false;
            active.discardsThisTurn = 0;

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
        /// Slots objetivo de una pasiva dirigida sobre <paramref name="board"/>, según su
        /// <see cref="PassiveEffect.mode"/>/<see cref="PassiveEffect.count"/> (espejo del targeting de
        /// ataque, §6). Frontmost/Backmost están anclados a la formación (deterministas); los slots
        /// vacíos que devuelva los filtran los callers (TurnDamage/TurnStatus/Regeneration).
        /// </summary>
        private List<int> PassiveTargets(PassiveEffect pe, int srcIdx, PlayerState board)
        {
            if (pe.mode == TargetMode.Self || pe.target == PassiveTarget.Self)
                return new List<int> { srcIdx };
            return ResolveTargets(pe.mode, pe.count, board.unitSlots, srcIdx);
        }

        // ── Fase 3: ACCIÓN ────────────────────────────────────────────────────────

        public ActionResult PlayCard(int handIndex, int deploySlot = -1, int effectTargetSlot = -1,
                                     int effectTargetSlotB = -1)
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;

            PlayerState active = ActivePlayer;
            PlayerState opp = OpponentPlayer;

            if (handIndex < 0 || handIndex >= active.hand.Count) return ActionResult.IndexOutOfRange;
            CardData card = active.hand[handIndex];
            int inflation = InflationPercent;
            if (!active.CanAfford(card, inflation)) return ActionResult.CannotAfford;

            if (card is UnitCardData unit)
            {
                int slot = ResolveDeploySlot(unit, active, deploySlot, out ActionResult deployResult);
                if (deployResult != ActionResult.Success) return deployResult;

                active.Pay(card, inflation);
                active.unitSlots[slot] = new UnitSlot(unit);
            }
            else if (card is EquipmentCardData equip)
            {
                if (!active.HasAnyUnit()) return ActionResult.InvalidTarget;  // sin portador, no se puede jugar
                if (effectTargetSlot < 0) return ActionResult.NeedsEffectTarget;
                if (effectTargetSlot >= active.unitSlots.Length || active.unitSlots[effectTargetSlot] == null)
                    return ActionResult.InvalidTarget;

                active.Pay(card, inflation);
                active.unitSlots[effectTargetSlot].Attach(equip);
            }
            else if (card is ActionCardData action)
            {
                ActionResult pre = ValidateEffectTargets(action, active, opp, effectTargetSlot, effectTargetSlotB);
                if (pre != ActionResult.Success) return pre;

                active.Pay(card, inflation);
                foreach (CardEffect effect in action.effects)
                    ResolveEffect(effect, active, opp, effectTargetSlot, effectTargetSlotB);
            }

            MoveToDiscard(active, handIndex);
            CheckVictory();
            return ActionResult.Success;
        }

        public ActionResult DiscardCard(int handIndex)
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;

            PlayerState active = ActivePlayer;
            if (handIndex < 0 || handIndex >= active.hand.Count) return ActionResult.IndexOutOfRange;
            if (active.discardsThisTurn >= 1) return ActionResult.DiscardLimitReached;

            MoveToDiscard(active, handIndex);
            active.discardsThisTurn++;
            return ActionResult.Success;
        }

        public bool CanDiscard => Phase == GamePhase.AwaitingAction && ActivePlayer.discardsThisTurn < 1;

        public ActionResult AttackWithUnit(int attackerSlot, int[] chosenTargets = null)
        {
            if (Phase != GamePhase.AwaitingAction) return ActionResult.WrongPhase;
            if (!CanAttackThisTurn) return ActionResult.CannotAttackFirstTurn;

            PlayerState active = ActivePlayer;
            PlayerState opp = OpponentPlayer;

            if (attackerSlot < 0 || attackerSlot >= active.unitSlots.Length) return ActionResult.IndexOutOfRange;
            UnitSlot attacker = active.unitSlots[attackerSlot];
            if (attacker == null) return ActionResult.NoUnitInSlot;
            if (attacker.IsStunned) return ActionResult.UnitStunned;
            if (attacker.attackedThisTurn) return ActionResult.AlreadyAttacked;  // una vez por unidad (spec §6)
            int attackCost = AttackCost(attackerSlot);  // ⚡ proporcional al daño total por objetivo (spec §3/§6)
            if (active.GetResource(ResourceType.Fuerza) < attackCost)
                return ActionResult.CannotAfford;

            UnitAttack ua = attacker.unit.attack;
            // Targeting anclado a la formación del objetivo (spec §6): rival si daña, propio si cura.
            PlayerState targetBoard = ua.IsHeal ? active : opp;
            List<int> candidates = ResolveTargets(ua.mode, ua.count, targetBoard.unitSlots, attackerSlot);

            int[] targets;
            if (!ua.RequiresChoice)
            {
                targets = candidates.ToArray();
            }
            else
            {
                // Snipe (Any): el jugador elige exactamente `count` unidades de entre las ocupadas.
                if (chosenTargets == null || chosenTargets.Length != ua.count)
                    return ActionResult.NeedsAttackTarget;
                foreach (int t in chosenTargets)
                    if (!candidates.Contains(t)) return ActionResult.InvalidTarget;
                targets = chosenTargets;
            }

            // Regla de objetivos (spec §6): la acción se CANCELA (sin gastar el ataque) si no afecta a
            // ningún objetivo válido. Si afecta al menos a uno, se permite aunque otros golpes whiffeen.
            if (!HasValidTarget(ua, targets, active, opp)) return ActionResult.InvalidTarget;
            attacker.attackedThisTurn = true;  // consume el ataque de esta unidad (spec §6)
            active.AddResource(ResourceType.Fuerza, -attackCost, _config.maxResource);  // atacar cuesta ⚡ proporcional

            // Multi-hit (spec §7.2): el golpe se aplica `hits` veces al MISMO objetivo. Reparte el daño/cura
            // total en varios pegues, disparando Espinas/Blindaje por cada uno. hits=1 = comportamiento clásico.
            int hits = ua.EffectiveHits;

            if (ua.IsHeal)
            {
                for (int h = 0; h < hits; h++)
                    foreach (int t in targets)
                    {
                        if (t < 0 || t >= active.unitSlots.Length) continue;
                        UnitSlot u = active.unitSlots[t];
                        if (u != null && u.currentHp < u.MaxHp)
                            u.currentHp = Math.Min(u.MaxHp, u.currentHp + ua.damagePerSlot);  // whiff si vacío/llena
                    }
                return ActionResult.Success;
            }

            // Daño efectivo POR GOLPE (base + equipo + Furia + Aura − Desmoralizar) + Retaliate de los
            // defensores, ACUMULADO por cada hit (Espinas pega una vez por golpe — el punto del multi-hit).
            int dmg = EffectiveAttackDamage(active.unitSlots, attackerSlot);
            int retaliation = 0;
            for (int h = 0; h < hits; h++)
            {
                foreach (int t in targets)
                {
                    if (t < 0 || t >= opp.unitSlots.Length) continue;
                    UnitSlot def = opp.unitSlots[t];
                    if (def == null) continue;  // whiff (slot vacío o ya muerto en un golpe previo)
                    foreach (PassiveEffect pe in def.AllPassives())
                        if (pe.passiveType == PassiveType.Retaliate) retaliation += pe.value;
                    int taken = dmg - ArmorOf(def);  // Blindaje mitiga CADA golpe (spec §7.3)
                    if (taken < 0) taken = 0;
                    def.currentHp -= taken;
                    if (def.IsDead) KillUnit(opp, t);
                }
            }

            // Chorro (PushBack): empuja a los objetivos sobrevivientes al fondo del rival (antes del Retaliate).
            if (active.unitSlots[attackerSlot] != null && HasPassive(attacker, PassiveType.PushBack))
                foreach (int t in targets) PushTargetBack(opp, t);

            if (retaliation > 0 && active.unitSlots[attackerSlot] != null)
            {
                attacker.currentHp -= retaliation;
                if (attacker.IsDead) KillUnit(active, attackerSlot);
            }

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
            // REPONER MANO (spec §6 paso 4): rellenar la mano del activo a handSize.
            RefillHand(ActivePlayer);

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
                    if (s.IsDead) KillUnit(p, i);
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
                        if (u.IsDead) KillUnit(tgt, slot);
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

        /// <summary>
        /// Resuelve las posiciones objetivo (0–5) sobre <paramref name="board"/> según el
        /// <see cref="TargetMode"/>, anclado a la formación (spec §6). El frente es el extremo de
        /// índice alto (cerca del rival); Frontmost cuenta desde ahí hacia el fondo. Puede devolver
        /// slots vacíos en los alcances profundos de Frontmost/Backmost (whiff), pero el slot ancla
        /// siempre está ocupado si el tablero no está vacío (invariante anti-deadlock).
        /// </summary>
        public static List<int> ResolveTargets(TargetMode mode, int count, UnitSlot[] board, int srcIdx)
        {
            var list = new List<int>();
            if (board == null) return list;
            int n = board.Length;
            switch (mode)
            {
                case TargetMode.Self:
                    if (srcIdx >= 0 && srcIdx < n) list.Add(srcIdx);
                    break;

                case TargetMode.Adjacent:
                    if (srcIdx - 1 >= 0 && srcIdx - 1 < n) list.Add(srcIdx - 1);
                    if (srcIdx + 1 >= 0 && srcIdx + 1 < n) list.Add(srcIdx + 1);
                    break;

                case TargetMode.All:
                case TargetMode.Any:   // candidatos: todas las ocupadas (la elección/auto-pick ocurre fuera)
                    for (int i = 0; i < n; i++) if (board[i] != null) list.Add(i);
                    break;

                case TargetMode.Frontmost:
                {
                    int f = ForemostOccupied(board);
                    if (f < 0) break;
                    int reach = count <= 0 ? 1 : count;
                    for (int k = 0; k < reach; k++) { int idx = f - k; if (idx < 0) break; list.Add(idx); }
                    break;
                }

                case TargetMode.Backmost:
                {
                    int b = BackmostOccupied(board);
                    if (b < 0) break;
                    int reach = count <= 0 ? 1 : count;
                    for (int k = 0; k < reach; k++) { int idx = b + k; if (idx >= n) break; list.Add(idx); }
                    break;
                }
            }
            return list;
        }

        /// <summary>Unidad más adelantada ocupada (mayor índice = más cerca del rival); -1 si vacío.</summary>
        private static int ForemostOccupied(UnitSlot[] board)
        {
            for (int i = board.Length - 1; i >= 0; i--) if (board[i] != null) return i;
            return -1;
        }

        /// <summary>Unidad más atrasada ocupada (menor índice = fondo); -1 si vacío.</summary>
        private static int BackmostOccupied(UnitSlot[] board)
        {
            for (int i = 0; i < board.Length; i++) if (board[i] != null) return i;
            return -1;
        }

        /// <summary>Suma de AuraDamage de aliadas cuyo objetivo cubre al atacante en <paramref name="slotIndex"/>.</summary>
        public int AuraBonusFor(UnitSlot[] board, int slotIndex)
        {
            int total = 0;
            for (int srcIdx = 0; srcIdx < board.Length; srcIdx++)
            {
                UnitSlot src = board[srcIdx];
                if (src == null || srcIdx == slotIndex) continue;
                foreach (PassiveEffect pe in src.AllPassives())
                    if (pe.passiveType == PassiveType.AuraDamage && pe.target == PassiveTarget.Allies
                        && ResolveTargets(pe.mode, pe.count, board, srcIdx).Contains(slotIndex))
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

        /// <summary>Suma del Blindaje (reducción de daño de ataque) de la unidad, incl. equipo (spec §7.3).</summary>
        public static int ArmorOf(UnitSlot u)
        {
            int a = 0;
            foreach (PassiveEffect pe in u.AllPassives())
                if (pe.passiveType == PassiveType.Armor) a += pe.value;
            return a;
        }

        private static bool HasPassive(UnitSlot u, PassiveType type)
        {
            foreach (PassiveEffect pe in u.AllPassives())
                if (pe.passiveType == type) return true;
            return false;
        }

        /// <summary>
        /// Chorro: empuja la unidad del slot <paramref name="t"/> al slot libre más al fondo (menor
        /// índice) de <paramref name="board"/>. No-op si no hay un slot libre más atrás. Ignora
        /// <c>allowedSlots</c> (empuje involuntario, como SwapUnits).
        /// </summary>
        private static void PushTargetBack(PlayerState board, int t)
        {
            if (t < 0 || t >= board.unitSlots.Length || board.unitSlots[t] == null) return;
            for (int j = 0; j < t; j++)
            {
                if (board.unitSlots[j] != null) continue;
                board.unitSlots[j] = board.unitSlots[t];
                board.unitSlots[t] = null;
                return;
            }
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
            if (u.IsDead) KillUnit(owner, slot);
        }

        /// <summary>
        /// Punto único de "muerte" de una unidad: libera el slot y dispara sus pasivas
        /// <see cref="PassiveType.OnDeath"/> (death-rattle, spec §7.3). Todas las fuentes de muerte
        /// (ataque, Poison, ModifyHP, muerte súbita) pasan por acá. Libera el slot ANTES de disparar
        /// (la unidad no se afecta a sí misma). El death-rattle puede encadenar más muertes (vía
        /// <see cref="DirectDamage"/>); NO re-evalúa victoria: el caller lo hace tras su fase/acción.
        /// </summary>
        private void KillUnit(PlayerState owner, int slot)
        {
            UnitSlot dead = owner.unitSlots[slot];
            if (dead == null) return;
            owner.unitSlots[slot] = null;

            PlayerState other = ReferenceEquals(owner, _players[0]) ? _players[1] : _players[0];
            foreach (PassiveEffect pe in dead.AllPassives())
                if (pe.passiveType == PassiveType.OnDeath)
                    ResolveOnDeath(pe, owner, other, slot);
        }

        /// <summary>
        /// Resuelve un death-rattle sobre los slots objetivo (mismo targeting que las pasivas
        /// dirigidas, §7.3): con <see cref="PassiveEffect.status"/> aplica el estado (ej. el Jubilado
        /// → Furia a aliados adyacentes); sin status, <see cref="PassiveEffect.value"/> de daño directo
        /// (ej. explosión a enemigos). El tablero lo decide <see cref="PassiveEffect.target"/>; whiff en slot vacío.
        /// </summary>
        private void ResolveOnDeath(PassiveEffect pe, PlayerState owner, PlayerState other, int slot)
        {
            PlayerState board = pe.target == PassiveTarget.Enemies ? other : owner;
            foreach (int t in PassiveTargets(pe, slot, board))
            {
                if (t < 0 || t >= board.unitSlots.Length || board.unitSlots[t] == null) continue;
                if (pe.status != null) board.unitSlots[t].activeStatuses.Add(pe.status.Clone());
                else if (pe.value != 0) DirectDamage(board, t, pe.value);
            }
        }

        // ── Mazo de robo (spec §8.1): mazo finito barajado, sin reemplazo, con rebaraje del descarte ──

        /// <summary>Baraja in-place con Fisher–Yates usando el RNG inyectado (determinista en tests).</summary>
        private void Shuffle(List<CardData> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// Roba del tope del mazo (final de la lista, O(1)). Si el mazo está vacío, rebaraja el descarte
        /// dentro del mazo y roba de ahí. Devuelve null sólo si mazo y descarte están ambos vacíos.
        /// </summary>
        private CardData DrawCard(PlayerState player)
        {
            if (player.deck.Count == 0)
            {
                if (player.discard.Count == 0) return null;
                player.deck.AddRange(player.discard);
                player.discard.Clear();
                Shuffle(player.deck);
            }
            int last = player.deck.Count - 1;
            CardData card = player.deck[last];
            player.deck.RemoveAt(last);
            return card;
        }

        /// <summary>
        /// Saca la carta jugada/descartada de la mano y la manda al descarte. NO roba: la mano se
        /// rellena recién al fin del turno (REPONER MANO, spec §6/§8.1), así no se cicla el mazo entero
        /// en un turno encadenando jugar→robar→jugar (con multi-carta).
        /// </summary>
        private void MoveToDiscard(PlayerState player, int handIndex)
        {
            player.discard.Add(player.hand[handIndex]);
            player.hand.RemoveAt(handIndex);
        }

        /// <summary>
        /// REPONER MANO (spec §6 paso 4): rellena la mano hasta <see cref="GameConfig.handSize"/> robando
        /// del tope del mazo (rebaraja el descarte si el mazo se vacía). Para si no quedan cartas para robar.
        /// </summary>
        private void RefillHand(PlayerState player)
        {
            while (player.hand.Count < _config.handSize)
            {
                CardData card = DrawCard(player);
                if (card == null) break;
                player.hand.Add(card);
            }
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
            return ActivePlayer.CanAfford(ActivePlayer.hand[handIndex], InflationPercent);
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

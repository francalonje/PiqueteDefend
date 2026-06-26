using System;
using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// IA greedy heurística (spec §16) — puerto a C# de <c>sim/policy.py</c>, adaptado al turno
    /// multi-acción (spec §6) y al costo de ataque en ⚡ (spec §3/§6). Determinista: los empates de
    /// puntaje se rompen por menor índice. Sin lookahead. Es el oponente del single-player (§17.5) y
    /// el mismo cerebro que el bot del simulador, así el balance del sim aplica al juego.
    ///
    /// <para>Se consulta vía <see cref="NextAction"/> repetidamente: juega TODAS las cartas que superen
    /// el umbral (re-evaluando) y ataca con cada unidad mientras le alcance la ⚡; cuando no conviene
    /// nada más, devuelve <see cref="PlannedAction.EndTurn"/>.</para>
    /// </summary>
    public sealed class HeuristicAiController : IPlayerController
    {
        /// <summary>Umbral de juego: una carta se juega sólo si su puntaje lo supera (spec §16.2).</summary>
        private const double Threshold = 0.0;

        private static readonly ResourceType[] AllResources =
            { ResourceType.Dinero, ResourceType.Fuerza, ResourceType.Social };

        public PlannedAction NextAction(GameEngine engine)
        {
            PlayerState p = engine.ActivePlayer;
            PlayerState opp = engine.OpponentPlayer;
            int infl = engine.InflationPercent;

            // 1) Fase de cartas: la mejor asequible que supere el umbral (re-evaluada en cada llamada).
            CardPlan best = null;
            for (int i = 0; i < p.hand.Count; i++)
            {
                if (!p.CanAfford(p.hand[i], infl)) continue;
                CardPlan plan = EvaluateCard(engine, p, opp, i);
                if (best == null || plan.score > best.score) best = plan;
            }
            if (best != null && best.score > Threshold)
                return PlannedAction.PlayCard(best.handIndex, best.deploySlot, best.effectSlot, best.effectSlotB);

            // 2) Fase de ataques: sólo si la regla de iniciativa lo permite (turno 1, §3/§16); BestAttack
            //    descarta los ataques que no se pueden pagar en ⚡ (costo proporcional, spec §6).
            if (engine.CanAttackThisTurn)
            {
                AttackPlan atk = BestAttack(engine, p, opp);
                if (atk != null && atk.value > 0)
                    return PlannedAction.Attack(atk.attackerSlot, atk.targets);
            }

            return PlannedAction.EndTurn();
        }

        // ── Valor de unidad (§16.1) ──────────────────────────────────────────

        private static double PassiveValue(IEnumerable<PassiveEffect> passives)
        {
            double total = 0;
            foreach (PassiveEffect pe in passives)
            {
                switch (pe.passiveType)
                {
                    case PassiveType.ProduceResource: total += 4.0 * pe.value; break;
                    case PassiveType.AuraDamage:
                    case PassiveType.Retaliate:
                    case PassiveType.Regeneration:
                    case PassiveType.TurnDamage:
                    case PassiveType.Armor: total += 3.0 * pe.value; break;
                    case PassiveType.TurnStatus: total += 4.0; break;
                    case PassiveType.OnDeath:
                        total += pe.status != null ? 2.0 * pe.status.value : 3.0 * pe.value; break;
                    case PassiveType.PushBack: total += 2.0; break;
                }
            }
            return total;
        }

        /// <summary>Golpes aproximados de un ataque, para estimar su valor (heurística §16.1).</summary>
        private static int AttackHits(UnitAttack a)
        {
            switch (a.mode)
            {
                case TargetMode.Frontmost:
                case TargetMode.Backmost:
                case TargetMode.Any: return Math.Max(1, a.count);
                case TargetMode.All: return 3;   // AoE: tablero típico aprox.
                default: return 1;
            }
        }

        private static double BaseUnitValue(UnitCardData u)
        {
            UnitAttack a = u.attack;
            double dmgTotal, pv;
            if (a.effect == AttackEffect.HealAllies)
            {
                dmgTotal = 0;
                pv = PassiveValue(u.passiveEffects) + 3.0 * a.damagePerSlot;
            }
            else
            {
                dmgTotal = (double)a.damagePerSlot * AttackHits(a);
                pv = PassiveValue(u.passiveEffects);
            }
            return u.maxHp / 4.0 + dmgTotal / 2.0 + pv;
        }

        private static double SlotValue(UnitSlot s)
        {
            UnitAttack a = s.unit.attack;
            double dmgTotal, pv;
            if (a.effect == AttackEffect.HealAllies)
            {
                dmgTotal = 0;
                pv = PassiveValue(s.AllPassives()) + 3.0 * a.damagePerSlot;
            }
            else
            {
                dmgTotal = (double)(a.damagePerSlot + s.EquipmentDamage) * AttackHits(a);
                pv = PassiveValue(s.AllPassives());
            }
            return s.MaxHp / 4.0 + dmgTotal / 2.0 + pv;
        }

        private static bool IsAttacker(UnitSlot s) =>
            s.unit.attack.effect == AttackEffect.DamageEnemies && s.unit.attack.damagePerSlot > 0;

        // ── Selección de objetivos (§16.5) ───────────────────────────────────

        private static int BestEnemyTarget(PlayerState opp, int lethal)
        {
            int best = -1;
            double bestVal = double.NegativeInfinity;
            if (lethal > 0)
            {
                for (int i = 0; i < opp.unitSlots.Length; i++)
                {
                    UnitSlot s = opp.unitSlots[i];
                    if (s == null || s.currentHp > lethal) continue;
                    double v = SlotValue(s);
                    if (v > bestVal) { bestVal = v; best = i; }
                }
                if (best >= 0) return best;   // prioriza matar a la pieza más cara
                bestVal = double.NegativeInfinity;
            }
            for (int i = 0; i < opp.unitSlots.Length; i++)
            {
                UnitSlot s = opp.unitSlots[i];
                if (s == null) continue;
                double v = SlotValue(s);
                if (v > bestVal) { bestVal = v; best = i; }
            }
            return best;
        }

        private static int BestUnit(PlayerState player, Func<UnitSlot, bool> filter)
        {
            int best = -1;
            double bestVal = double.NegativeInfinity;
            for (int i = 0; i < player.unitSlots.Length; i++)
            {
                UnitSlot s = player.unitSlots[i];
                if (s == null || !filter(s)) continue;
                double v = SlotValue(s);
                if (v > bestVal) { bestVal = v; best = i; }
            }
            return best;
        }

        private static int BestEnemyAttacker(PlayerState opp) => BestUnit(opp, IsAttacker);
        private static int BestOwnAttacker(PlayerState p) => BestUnit(p, IsAttacker);
        private static int BestDamagedAlly(PlayerState p) => BestUnit(p, s => s.currentHp < s.MaxHp);

        private static int FirstOccupied(PlayerState p)
        {
            for (int i = 0; i < p.unitSlots.Length; i++)
                if (p.unitSlots[i] != null) return i;
            return -1;
        }

        // ── Deploy (§16.6, adaptado: el C# no tiene "archetype", se infiere del rol) ──

        private static bool PrefersBackline(UnitCardData unit)
        {
            UnitAttack a = unit.attack;
            if (a.IsHeal || a.mode == TargetMode.Any) return true;   // healer / sniper: proteger
            if (a.effect == AttackEffect.DamageEnemies && a.damagePerSlot <= 0) return true;  // soporte sin daño
            foreach (PassiveEffect pe in unit.passiveEffects)
                if (pe.passiveType == PassiveType.ProduceResource
                    || pe.passiveType == PassiveType.TurnDamage
                    || pe.passiveType == PassiveType.TurnStatus) return true;  // productora / emisor
            return false;   // muro / escaramuza / cleave → frente (tankean / la posición no cambia su target)
        }

        private static int ChooseDeploySlot(PlayerState p, UnitCardData unit)
        {
            int chosen = -1;
            bool back = PrefersBackline(unit);
            for (int i = 0; i < p.unitSlots.Length; i++)
            {
                if (p.unitSlots[i] != null || !unit.AllowsSlot(i)) continue;
                if (chosen < 0) { chosen = i; continue; }
                if (back ? i < chosen : i > chosen) chosen = i;   // retaguardia = menor índice; frente = mayor
            }
            return chosen;   // -1 si no hay slot libre permitido → no se despliega (§8.3)
        }

        // ── Puntaje de cartas (§16.3) ────────────────────────────────────────

        private sealed class CardPlan
        {
            public double score;
            public int handIndex = -1;
            public int deploySlot = -1;
            public int effectSlot = -1;
            public int effectSlotB = -1;
        }

        private static CardPlan EvaluateCard(GameEngine engine, PlayerState p, PlayerState opp, int idx)
        {
            CardData card = p.hand[idx];

            if (card is UnitCardData unit)
            {
                int slot = ChooseDeploySlot(p, unit);
                if (slot < 0) return new CardPlan { score = double.NegativeInfinity };
                double s = BaseUnitValue(unit);
                if (p.AliveUnitCount() < 2) s += 6;   // necesidad de presencia
                return new CardPlan { score = s, handIndex = idx, deploySlot = slot };
            }

            if (card is EquipmentCardData equip)
            {
                int carrier = BestOwnAttacker(p);
                if (carrier < 0) carrier = FirstOccupied(p);
                if (carrier < 0) return new CardPlan { score = double.NegativeInfinity };
                double val = 0;
                foreach (StatModifier m in equip.statModifiers) val += m.value;
                val += PassiveValue(equip.grantedPassives);
                return new CardPlan { score = val, handIndex = idx, effectSlot = carrier };
            }

            var action = (ActionCardData)card;
            double score = 0;
            int effSlot = -1, effSlotB = -1;
            foreach (CardEffect eff in action.effects)
            {
                switch (eff.effectType)
                {
                    case CardEffectType.ModifyResource:
                    {
                        ResourceType res = eff.resourceTarget;
                        if (eff.target == TargetType.Self && eff.value > 0)
                        {
                            double sv = eff.value * (ResourceShort(p, res) ? 1.5 : 1.0);
                            if (p.GetResource(res) >= engine.Config.maxResource - eff.value) sv *= 0.2;
                            score += sv;
                        }
                        else
                        {
                            score += Math.Min(Math.Abs(eff.value), opp.GetResource(res)) * 0.5;
                        }
                        break;
                    }
                    case CardEffectType.ModifyHP:
                    {
                        if (eff.target == TargetType.Opponent && eff.value < 0)
                        {
                            int t = BestEnemyTarget(opp, Math.Abs(eff.value));
                            if (t >= 0)
                            {
                                UnitSlot d = opp.unitSlots[t];
                                double sv = Math.Min(Math.Abs(eff.value), d.currentHp);
                                if (Math.Abs(eff.value) >= d.currentHp) sv += SlotValue(d);
                                score += sv; effSlot = t;
                            }
                        }
                        else if (eff.target == TargetType.Self && eff.value > 0)
                        {
                            int t = BestDamagedAlly(p);
                            if (t >= 0)
                            {
                                UnitSlot u = p.unitSlots[t];
                                score += Math.Min(eff.value, u.MaxHp - u.currentHp); effSlot = t;
                            }
                        }
                        break;
                    }
                    case CardEffectType.ApplyStatus:
                    {
                        if (eff.status == null) break;
                        StatusEffect st = eff.status;
                        if (StatusEffect.IsPlayerStatus(st.statusType))
                        {
                            if (st.statusType == StatusType.DoubleProduction)
                                score += 3 + CountOwnProducers(p);   // producción proyectada
                            else
                                score += 3;                          // SkipProduction al rival
                        }
                        else if (st.statusType == StatusType.Stun)
                        {
                            int t = BestEnemyAttacker(opp);
                            if (t >= 0) { score += 0.5 * SlotValue(opp.unitSlots[t]); effSlot = t; }
                        }
                        else if (st.statusType == StatusType.Poison)
                        {
                            int t = BestEnemyTarget(opp, 0);
                            if (t >= 0) { score += (double)st.value * st.counter; effSlot = t; }
                        }
                        else if (st.statusType == StatusType.Desmoralizar)
                        {
                            int t = BestEnemyAttacker(opp);
                            if (t >= 0) { score += st.value * st.counter * 0.4; effSlot = t; }
                        }
                        else if (st.statusType == StatusType.Furia)
                        {
                            int t = BestOwnAttacker(p);
                            if (t >= 0) { score += st.value * st.counter * 0.5; effSlot = t; }
                        }
                        break;
                    }
                    case CardEffectType.MoveUnit:
                    {
                        if (PlanMove(p, out int src, out int dst)) { effSlot = src; effSlotB = dst; score += 0.1; }
                        break;
                    }
                    case CardEffectType.SwapUnits:
                    {
                        if (PlanSwapEnemy(opp, out int a2, out int b2)) { effSlot = a2; effSlotB = b2; score += 0.1; }
                        break;
                    }
                }
            }
            return new CardPlan { score = score, handIndex = idx, effectSlot = effSlot, effectSlotB = effSlotB };
        }

        private static bool ResourceShort(PlayerState p, ResourceType res)
        {
            int v = p.GetResource(res);
            foreach (ResourceType r in AllResources)
                if (p.GetResource(r) < v) return false;   // hay otro menor → res no es el más corto
            return true;
        }

        private static int CountOwnProducers(PlayerState p)
        {
            int n = 0;
            foreach (UnitSlot s in p.unitSlots)
            {
                if (s == null) continue;
                foreach (PassiveEffect pe in s.AllPassives())
                    if (pe.passiveType == PassiveType.ProduceResource) n++;
            }
            return n;
        }

        private static bool PlanMove(PlayerState p, out int src, out int dst)
        {
            for (int s = 0; s < p.unitSlots.Length; s++)
            {
                if (p.unitSlots[s] == null) continue;
                for (int d = 0; d < p.unitSlots.Length; d++)
                    if (d != s && p.unitSlots[d] == null && p.unitSlots[s].unit.AllowsSlot(d))
                    { src = s; dst = d; return true; }
            }
            src = -1; dst = -1; return false;
        }

        private static bool PlanSwapEnemy(PlayerState opp, out int front, out int back)
        {
            front = -1; back = -1;
            for (int i = 0; i < opp.unitSlots.Length; i++)
            {
                if (opp.unitSlots[i] == null) continue;
                if (back < 0) back = i;     // primer ocupado = más al fondo (menor índice)
                front = i;                  // último ocupado = más al frente (mayor índice)
            }
            return front >= 0 && back >= 0 && front != back;
        }

        // ── Elección de ataque (§16.4) ───────────────────────────────────────

        private sealed class AttackPlan
        {
            public int attackerSlot;
            public int[] targets;   // null = modos anclados; lista = snipe (Any)
            public double value;
        }

        private static double TargetContribution(PlayerState opp, int dmg, int t)
        {
            if (t < 0 || t >= opp.unitSlots.Length) return 0;
            UnitSlot d = opp.unitSlots[t];
            if (d == null) return 0;   // whiff
            int retal = 0;
            foreach (PassiveEffect pe in d.AllPassives())
                if (pe.passiveType == PassiveType.Retaliate) retal += pe.value;
            double val = Math.Min(dmg, d.currentHp) - retal;
            if (dmg >= d.currentHp) val += SlotValue(d);
            return val;
        }

        private static int[] PadTargets(List<int> chosen, List<int> candidates, int pick)
        {
            var outl = new List<int>(chosen);
            foreach (int c in candidates)
            {
                if (outl.Count >= pick) break;
                if (!outl.Contains(c)) outl.Add(c);
            }
            if (outl.Count > pick) outl = outl.GetRange(0, pick);
            return outl.ToArray();
        }

        private static AttackPlan BestAttack(GameEngine engine, PlayerState p, PlayerState opp)
        {
            AttackPlan best = null;
            for (int i = 0; i < p.unitSlots.Length; i++)
            {
                UnitSlot s = p.unitSlots[i];
                if (s == null || s.IsStunned || s.attackedThisTurn) continue;
                if (p.GetResource(ResourceType.Fuerza) < engine.AttackCost(i))
                    continue;   // no alcanza la ⚡: mismo costo que cobra el motor (proporcional al
                                // daño TOTAL por objetivo = daño × golpes, multi-hit incluido, spec §6/§7.2)
                UnitAttack a = s.unit.attack;
                UnitSlot[] targetBoard = a.IsHeal ? p.unitSlots : opp.unitSlots;
                List<int> candidates = GameEngine.ResolveTargets(a.mode, a.count, targetBoard, i);

                if (a.IsHeal)
                {
                    var heals = new List<KeyValuePair<int, int>>();
                    foreach (int t in candidates)
                    {
                        UnitSlot u = p.unitSlots[t];
                        if (u == null) continue;
                        int h = Math.Min(a.damagePerSlot, u.MaxHp - u.currentHp);
                        if (h > 0) heals.Add(new KeyValuePair<int, int>(t, h));
                    }
                    if (heals.Count == 0) continue;
                    int[] chosen; double value;
                    if (a.RequiresChoice)
                    {
                        heals.Sort((x, y) => x.Value != y.Value ? y.Value.CompareTo(x.Value) : x.Key.CompareTo(y.Key));
                        int take = Math.Min(a.count, heals.Count);
                        var pick = new List<int>();
                        value = 0;
                        for (int k = 0; k < take; k++) { pick.Add(heals[k].Key); value += heals[k].Value; }
                        chosen = PadTargets(pick, candidates, a.count);
                    }
                    else
                    {
                        chosen = null; value = 0;
                        foreach (var hh in heals) value += hh.Value;
                    }
                    if (value > 0 && (best == null || value > best.value))
                        best = new AttackPlan { attackerSlot = i, targets = chosen, value = value };
                    continue;
                }

                int dmg = engine.EffectiveAttackDamage(p.unitSlots, i);
                if (dmg <= 0) continue;
                int[] chosen2; double value2;
                if (a.RequiresChoice)
                {
                    var ranked = new List<int>(candidates);
                    ranked.Sort((x, y) =>
                    {
                        double cx = TargetContribution(opp, dmg, x), cy = TargetContribution(opp, dmg, y);
                        return cx != cy ? cy.CompareTo(cx) : x.CompareTo(y);
                    });
                    int take = Math.Min(a.count, ranked.Count);
                    var pick = new List<int>();
                    value2 = 0;
                    for (int k = 0; k < take; k++) { pick.Add(ranked[k]); value2 += TargetContribution(opp, dmg, ranked[k]); }
                    chosen2 = PadTargets(pick, candidates, a.count);
                }
                else
                {
                    chosen2 = null; value2 = 0;
                    foreach (int t in candidates) value2 += TargetContribution(opp, dmg, t);
                }
                if (value2 > 0 && (best == null || value2 > best.value))
                    best = new AttackPlan { attackerSlot = i, targets = chosen2, value = value2 };
            }
            return best;
        }
    }
}

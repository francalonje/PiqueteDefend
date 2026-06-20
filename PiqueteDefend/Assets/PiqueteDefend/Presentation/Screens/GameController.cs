using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Pantalla de juego (spec §11.3). Conecta el <see cref="GameEngine"/> (lógica pura) con la UI:
    /// renderiza stats/slots/mano, jugar/descartar por drag&amp;drop, atacar por click→popover, y el
    /// fin de turno explícito. Hotseat: al terminar el turno arranca automáticamente el del rival.
    ///
    /// UI mínima de MVP: las unidades son rectángulos con nombre + HP.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameController : MonoBehaviour
    {
        private const string PanelSettingsResource = "UIPanelSettings";
        private const string CatalogResource = "CardCatalog";

        private enum Mode { Acting, AwaitDeploySlot, AwaitEffectTarget, AwaitAttackTarget, Finished }

        private GameEngine _engine;
        private Mode _mode = Mode.Acting;
        private int _pendingCard = -1;
        private int _pendingAttacker = -1;
        private TargetType _pendingEffectSide;

        // Drag & drop
        private bool _dragging;
        private int _dragIndex = -1;
        private VisualElement _ghost;

        // UI refs
        private VisualElement _root, _hand, _p0Slots, _p1Slots, _overlay, _playZone, _discardZone;
        private Button _popover;
        private Label _turnBanner, _hint, _overlayTitle, _overlayMsg;
        private Button _overlayPrimary, _overlaySecondary, _endTurnButton;
        private readonly Label[] _faction = new Label[2];
        private readonly Label[] _res = new Label[2];
        private readonly Label[] _status = new Label[2];

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (doc.panelSettings == null)
                doc.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);

            _root = doc.rootVisualElement;
            if (_root == null) return;

            CacheRefs(_root);
            ApplyBackground(_root);
            BuildPopover();

            var catalog = Resources.Load<CardCatalog>(CatalogResource);
            if (catalog == null)
            {
                Debug.LogError("[GameController] No se encontró CardCatalog en Resources.");
                return;
            }

            _engine = new GameEngine(new GameConfig(), new SystemRandomProvider(), catalog);
            _engine.StartGame(MatchConfig.Player0, MatchConfig.Player1);

            _endTurnButton.clicked += OnEndTurnButton;

            AudioManager.Instance?.PlayMusic(AudioId.MusicGame);
            BeginActiveTurn();
        }

        private void CacheRefs(VisualElement root)
        {
            _turnBanner = root.Q<Label>("turn-banner");
            _hint = root.Q<Label>("hint");
            _hand = root.Q<VisualElement>("hand");
            _p0Slots = root.Q<VisualElement>("p0-slots");
            _p1Slots = root.Q<VisualElement>("p1-slots");
            _playZone = root.Q<VisualElement>("play-zone");
            _discardZone = root.Q<VisualElement>("discard-zone");
            _endTurnButton = root.Q<Button>("end-turn-button");

            _overlay = root.Q<VisualElement>("overlay");
            _overlayTitle = root.Q<Label>("overlay-title");
            _overlayMsg = root.Q<Label>("overlay-msg");
            _overlayPrimary = root.Q<Button>("overlay-primary");
            _overlaySecondary = root.Q<Button>("overlay-secondary");

            _faction[0] = root.Q<Label>("p0-faction"); _faction[1] = root.Q<Label>("p1-faction");
            _res[0] = root.Q<Label>("p0-res"); _res[1] = root.Q<Label>("p1-res");
            _status[0] = root.Q<Label>("p0-status"); _status[1] = root.Q<Label>("p1-status");
        }

        private void BuildPopover()
        {
            _popover = new Button { text = "⚔ Atacar" };
            _popover.AddToClassList("attack-popover");
            _popover.style.position = Position.Absolute;
            _popover.style.display = DisplayStyle.None;
            _popover.clicked += OnPopoverClicked;
            _root.Add(_popover);
        }

        private static void ApplyBackground(VisualElement root)
        {
            SetBackground(root.Q<VisualElement>("bg-left"), "bg-left");
            SetBackground(root.Q<VisualElement>("bg-right"), "bg-right");
        }

        private static void SetBackground(VisualElement element, string resourceName)
        {
            if (element == null) return;
            var texture = Resources.Load<Texture2D>(resourceName);
            if (texture != null) { element.style.backgroundImage = new StyleBackground(texture); return; }
            var sprite = Resources.Load<Sprite>(resourceName);
            if (sprite != null) element.style.backgroundImage = new StyleBackground(sprite);
        }

        // ── Flujo de turno ────────────────────────────────────────────────────

        private void BeginActiveTurn()
        {
            _engine.BeginTurn();   // EFECTOS + PRODUCCIÓN + chequeo de victoria
            if (_engine.IsFinished) { ShowOutcome(); return; }
            _mode = Mode.Acting;
            _pendingCard = -1;
            _pendingAttacker = -1;
            Render();
        }

        private void OnEndTurnButton()
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);

            if (_mode != Mode.Acting) { CancelTargeting(); return; }

            _engine.EndTurn();
            if (_engine.IsFinished) { ShowOutcome(); return; }
            BeginActiveTurn();
        }

        /// <summary>Vuelve a Acting tras una acción (carta o ataque) sin terminar el turno.</summary>
        private void AfterAction()
        {
            if (_engine.IsFinished) { ShowOutcome(); return; }
            _mode = Mode.Acting;
            _pendingCard = -1;
            Render();
        }

        private void CancelTargeting()
        {
            _mode = Mode.Acting;
            _pendingCard = -1;
            _pendingAttacker = -1;
            HidePopover();
            Render();
        }

        // ── Jugar / descartar (drag & drop) ─────────────────────────────────────

        private void TryPlayCard(int index)
        {
            if (_engine.CardActionUsed) return;
            if (!_engine.CanAfford(index)) { _hint.text = "No te alcanzan los recursos"; return; }

            if (_engine.RequiresDeploySlot(index))
            {
                _pendingCard = index;
                _mode = Mode.AwaitDeploySlot;
                _hint.text = "Elegí un slot propio para reemplazar";
                _endTurnButton.text = "Cancelar";
                RenderSlots();
                return;
            }

            if (_engine.RequiresEffectTarget(index))
            {
                _pendingCard = index;
                _pendingEffectSide = EffectSide(_engine.ActivePlayer.hand[index]);
                _mode = Mode.AwaitEffectTarget;
                _hint.text = _pendingEffectSide == TargetType.Self
                    ? "Elegí una unidad propia"
                    : "Elegí una unidad enemiga";
                _endTurnButton.text = "Cancelar";
                RenderSlots();
                return;
            }

            ResolveCardPlay(_engine.PlayCard(index));
        }

        private void DoDiscard(int index)
        {
            if (_engine.CardActionUsed) return;
            ResolveCardPlay(_engine.DiscardCard(index));
        }

        private void ResolveCardPlay(ActionResult result)
        {
            if (result != ActionResult.Success) { _hint.text = "No se pudo jugar la carta"; return; }
            AfterAction();
        }

        private void OnDeploySlotChosen(int slot) => ResolveCardPlay(_engine.PlayCard(_pendingCard, deploySlot: slot));
        private void OnEffectTargetChosen(int slot) => ResolveCardPlay(_engine.PlayCard(_pendingCard, effectTargetSlot: slot));

        // ── Atacar (click unidad → popover → atacar) ────────────────────────────

        private void OnOwnUnitClicked(int slot, VisualElement slotEl)
        {
            if (_engine.AttackUsed) { _hint.text = "Ya atacaste este turno"; return; }
            AudioManager.Instance?.PlaySfx(AudioId.CardClick);
            ShowPopover(slot, slotEl);
        }

        private void ShowPopover(int attackerSlot, VisualElement slotEl)
        {
            _pendingAttacker = attackerSlot;
            UnitAttack ua = _engine.ActivePlayer.unitSlots[attackerSlot].unit.attack;
            _popover.text = ua.RequiresChoice
                ? $"⚔ Atacar (elegí 1) · {ua.damagePerSlot}"
                : $"⚔ Atacar · {ua.damagePerSlot}";

            Rect wb = slotEl.worldBound;
            _popover.style.left = wb.x;
            _popover.style.top = Mathf.Max(0f, wb.y - 44f);
            _popover.style.display = DisplayStyle.Flex;
        }

        private void HidePopover() => _popover.style.display = DisplayStyle.None;

        private void OnPopoverClicked()
        {
            if (_pendingAttacker < 0 || _engine.AttackUsed) { HidePopover(); return; }

            if (_engine.AttackRequiresTarget(_pendingAttacker))
            {
                _mode = Mode.AwaitAttackTarget;
                HidePopover();
                _hint.text = "Elegí el slot enemigo a atacar";
                _endTurnButton.text = "Cancelar";
                RenderSlots();
                return;
            }

            ResolveAttack(_engine.AttackWithUnit(_pendingAttacker));
        }

        private void OnAttackTargetChosen(int slot) => ResolveAttack(_engine.AttackWithUnit(_pendingAttacker, new[] { slot }));

        private void ResolveAttack(ActionResult result)
        {
            HidePopover();
            if (result != ActionResult.Success) { _hint.text = "Ataque inválido"; _mode = Mode.Acting; Render(); return; }
            _pendingAttacker = -1;
            AfterAction();
        }

        // ── Render ──────────────────────────────────────────────────────────────

        private void Render()
        {
            HidePopover();
            _turnBanner.text = $"Turno: {Display(_engine.ActivePlayer.faction)}";
            _hint.text = string.Empty;
            _endTurnButton.text = "Terminar turno";
            RenderPanel(0);
            RenderPanel(1);
            RenderSlots();
            RenderHand();
            UpdateZones();
        }

        private void RenderPanel(int index)
        {
            PlayerState p = _engine.PlayerAt(index);
            bool active = _engine.ActiveIndex == index;
            _faction[index].text = (active ? "▶ " : "") + Display(p.faction);
            _res[index].text = $"$ {p.dinero}   ⚡ {p.fuerza}   📣 {p.social}";
            _status[index].text = StatusText(p);
        }

        private void RenderSlots()
        {
            RenderSlotColumn(_p0Slots, 0);
            RenderSlotColumn(_p1Slots, 1);
        }

        private void RenderSlotColumn(VisualElement column, int playerIndex)
        {
            column.Clear();
            PlayerState p = _engine.PlayerAt(playerIndex);
            int activeIdx = _engine.ActiveIndex;

            for (int i = 0; i < p.unitSlots.Length; i++)
            {
                UnitSlot slot = p.unitSlots[i];
                var el = new VisualElement();
                el.AddToClassList("slot");

                if (slot == null)
                {
                    el.AddToClassList("slot--empty");
                    el.Add(new Label("—"));
                }
                else
                {
                    var name = new Label(slot.unit.cardName); name.AddToClassList("slot__name");
                    var hp = new Label($"HP {slot.currentHp}/{slot.MaxHp}"); hp.AddToClassList("slot__meta");
                    el.Add(name); el.Add(hp);
                }

                WireSlot(el, playerIndex, activeIdx, i, slot);
                column.Add(el);
            }
        }

        private void WireSlot(VisualElement el, int playerIndex, int activeIdx, int slotIndex, UnitSlot slot)
        {
            int captured = slotIndex;

            switch (_mode)
            {
                case Mode.Acting:
                    if (playerIndex == activeIdx && slot != null && !_engine.AttackUsed)
                    {
                        el.AddToClassList("slot--attacker");
                        el.RegisterCallback<ClickEvent>(ev => { ev.StopPropagation(); OnOwnUnitClicked(captured, el); });
                    }
                    break;

                case Mode.AwaitDeploySlot:
                    if (playerIndex == activeIdx)
                    {
                        el.AddToClassList("slot--target");
                        el.RegisterCallback<ClickEvent>(_ => OnDeploySlotChosen(captured));
                    }
                    break;

                case Mode.AwaitEffectTarget:
                    int side = _pendingEffectSide == TargetType.Self ? activeIdx : 1 - activeIdx;
                    if (playerIndex == side && slot != null)
                    {
                        el.AddToClassList("slot--target");
                        el.RegisterCallback<ClickEvent>(_ => OnEffectTargetChosen(captured));
                    }
                    break;

                case Mode.AwaitAttackTarget:
                    if (playerIndex == 1 - activeIdx)
                    {
                        el.AddToClassList("slot--target");
                        el.RegisterCallback<ClickEvent>(_ => OnAttackTargetChosen(captured));
                    }
                    break;
            }
        }

        private void RenderHand()
        {
            _hand.Clear();
            PlayerState active = _engine.ActivePlayer;
            bool cardUsed = _engine.CardActionUsed;

            for (int i = 0; i < active.hand.Count; i++)
            {
                CardData card = active.hand[i];
                var el = new VisualElement();
                el.AddToClassList("card");
                if (!active.CanAfford(card)) el.AddToClassList("card--unaffordable");
                if (cardUsed) el.AddToClassList("card--used");

                var name = new Label(card.cardName); name.AddToClassList("card__name");
                var cost = new Label(CostText(card)); cost.AddToClassList("card__cost");
                var body = new Label(EffectText(card)); body.AddToClassList("card__body");
                el.Add(name); el.Add(cost); el.Add(body);

                if (!cardUsed) MakeDraggable(el, i);
                _hand.Add(el);
            }
        }

        private void UpdateZones()
        {
            SetZoneEnabled(_playZone, !_engine.CardActionUsed);
            SetZoneEnabled(_discardZone, !_engine.CardActionUsed);
        }

        private static void SetZoneEnabled(VisualElement zone, bool enabled)
        {
            if (enabled) zone.RemoveFromClassList("dropzone--disabled");
            else zone.AddToClassList("dropzone--disabled");
        }

        // ── Drag & drop ─────────────────────────────────────────────────────────

        private void MakeDraggable(VisualElement card, int index)
        {
            card.RegisterCallback<PointerDownEvent>(e => OnDragStart(e, index, card));
            card.RegisterCallback<PointerMoveEvent>(OnDragMove);
            card.RegisterCallback<PointerUpEvent>(e => OnDragEnd(e, index, card));
        }

        private void OnDragStart(PointerDownEvent e, int index, VisualElement card)
        {
            if (_mode != Mode.Acting || _engine.CardActionUsed) return;

            _dragging = true;
            _dragIndex = index;
            card.CapturePointer(e.pointerId);
            AudioManager.Instance?.PlaySfx(AudioId.CardClick);
            HidePopover();

            _ghost = new Label(_engine.ActivePlayer.hand[index].cardName) { pickingMode = PickingMode.Ignore };
            _ghost.AddToClassList("drag-ghost");
            _root.Add(_ghost);
            PositionGhost(e.position);
        }

        private void OnDragMove(PointerMoveEvent e)
        {
            if (!_dragging) return;
            PositionGhost(e.position);
        }

        private void OnDragEnd(PointerUpEvent e, int index, VisualElement card)
        {
            if (!_dragging || _dragIndex != index) return;

            _dragging = false;
            _dragIndex = -1;
            card.ReleasePointer(e.pointerId);
            if (_ghost != null) { _ghost.RemoveFromHierarchy(); _ghost = null; }

            Vector2 pos = e.position;
            if (_playZone.worldBound.Contains(pos)) TryPlayCard(index);
            else if (_discardZone.worldBound.Contains(pos)) DoDiscard(index);
        }

        private void PositionGhost(Vector2 pos)
        {
            if (_ghost == null) return;
            _ghost.style.left = pos.x - 55f;
            _ghost.style.top = pos.y - 30f;
        }

        // ── Overlay ──────────────────────────────────────────────────────────

        private void ShowOutcome()
        {
            _mode = Mode.Finished;
            HidePopover();
            GameOutcome o = _engine.Outcome.Value;

            string title = o.IsDraw ? "Empate" : $"Ganó {Display(o.Winner.Value)}";
            string msg = o.IsDraw ? "Muerte simultánea" : $"por {ConditionText(o.Condition)}";

            _overlayTitle.text = title;
            _overlayMsg.text = msg;

            WireOverlayButton(_overlayPrimary, "Revancha", () => SceneManager.LoadScene("Game"));
            WireOverlayButton(_overlaySecondary, "Menú principal", () => SceneManager.LoadScene("Main"));

            _overlay.style.display = DisplayStyle.Flex;
        }

        private static void WireOverlayButton(Button button, string text, Action onClick)
        {
            button.text = text;
            button.clickable = new Clickable(() =>
            {
                AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
                onClick?.Invoke();
            });
            button.style.display = DisplayStyle.Flex;
        }

        // ── Helpers de texto ────────────────────────────────────────────────────

        private TargetType EffectSide(CardData card)
        {
            if (card is ActionCardData a)
                foreach (CardEffect e in a.effects)
                    if (e.TargetsAUnitSlot && e.targetSlot < 0)
                        return e.target;
            return TargetType.Opponent;
        }

        private static string Display(Faction f) => f == Faction.Manifestantes ? "Manifestantes" : "Policías";

        private static string StatusText(PlayerState p)
        {
            if (p.activeStatuses.Count == 0) return string.Empty;
            var parts = new List<string>();
            foreach (StatusEffect s in p.activeStatuses)
                parts.Add(s.statusType == StatusType.SkipProduction ? "⏭ sin producción" : "x2 producción");
            return string.Join("  ", parts);
        }

        private static string CostText(CardData c)
        {
            if (c.costs == null || c.costs.Count == 0) return "gratis";
            var parts = new List<string>();
            foreach (ResourceCost rc in c.costs) parts.Add($"{rc.amount}{ResSym(rc.resource)}");
            return string.Join("  ", parts);
        }

        private static string EffectText(CardData c)
        {
            if (c is UnitCardData u)
            {
                string s = $"{u.maxHp} HP · pega {u.attack.damagePerSlot}";
                foreach (PassiveEffect p in u.passiveEffects)
                    if (p.passiveType == PassiveType.ProduceResource)
                        s += $"\n+{p.value}{ResSym(p.resource)}/turno";
                return s;
            }

            if (c is ActionCardData a)
            {
                var parts = new List<string>();
                foreach (CardEffect e in a.effects) parts.Add(EffectPart(e));
                return string.Join("\n", parts);
            }
            return string.Empty;
        }

        private static string EffectPart(CardEffect e)
        {
            switch (e.effectType)
            {
                case CardEffectType.ModifyHP:
                    return e.value < 0 ? $"−{-e.value} HP a una unidad" : $"+{e.value} HP a una unidad";
                case CardEffectType.ModifyResource:
                    string who = e.target == TargetType.Opponent ? " al rival" : "";
                    string sign = e.value >= 0 ? "+" : "";
                    return $"{sign}{e.value} {ResSym(e.resourceTarget)}{who}";
                case CardEffectType.RemoveUnit:
                    return "Elimina una unidad enemiga";
                case CardEffectType.ApplyStatus:
                    return e.status.statusType == StatusType.SkipProduction
                        ? "El rival saltea producción"
                        : "Duplicás tu producción";
                default:
                    return "";
            }
        }

        private static string ResSym(ResourceType r) => r switch
        {
            ResourceType.Dinero => "$",
            ResourceType.Fuerza => "⚡",
            ResourceType.Social => "📣",
            _ => ""
        };

        private static string ConditionText(WinCondition c) => c switch
        {
            WinCondition.KO => "KO",
            WinCondition.Timeout => "límite de turnos",
            WinCondition.Draw => "muerte simultánea",
            _ => ""
        };
    }
}

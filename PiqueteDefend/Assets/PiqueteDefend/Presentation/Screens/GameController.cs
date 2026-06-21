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
        private Vector2 _dragPointerOffset;

        // Slot element refs (rebuilt each Render — used for animations)
        private readonly VisualElement[] _p0SlotEls = new VisualElement[6];
        private readonly VisualElement[] _p1SlotEls = new VisualElement[6];

        // Pending attack animation state
        private int _animAttackerIdx = -1;
        private int _animAttackerPlayer = -1;
        private int[] _animOpponentSnap;
        private string _animHitSoundId;

        // UI refs
        private VisualElement _root, _hand, _p0Slots, _p1Slots, _overlay, _playZone, _discardZone;
        private Button _popover;
        private Label _turnChip, _hint, _overlayTitle, _overlayMsg;
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
            SceneBackground.Apply(_root, "bg");
            BuildPopover();

            var catalog = Resources.Load<CardCatalog>(CatalogResource);
            if (catalog == null)
            {
                Debug.LogError("[GameController] No se encontró CardCatalog en Resources.");
                return;
            }

            _engine = new GameEngine(new GameConfig(), new SystemRandomProvider(), catalog);
            // Manifestantes = jugador 0 (izquierda), Policías = jugador 1 (derecha). La selección
            // solo decide quién arranca, no los lados.
            int firstIndex = MatchConfig.StartingFaction == MatchConfig.Player0 ? 0 : 1;
            _engine.StartGame(MatchConfig.Player0, MatchConfig.Player1, firstIndex);

            _endTurnButton.clicked += OnEndTurnButton;

            AudioManager.Instance?.PlayMusic(AudioId.MusicGame);
            BeginActiveTurn();
        }

        private void CacheRefs(VisualElement root)
        {
            _turnChip = root.Q<Label>("turn-chip");
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

            CardData played = _engine.ActivePlayer.hand[index];
            ResolveCardPlay(_engine.PlayCard(index), played);
        }

        private void DoDiscard(int index)
        {
            if (_engine.CardActionUsed) return;
            ResolveCardPlay(_engine.DiscardCard(index));   // descartar no es "jugar": sin sonido de jugada
        }

        // playedCard != null → se reproduce el sonido de jugar carta (propio de la carta o el default).
        private void ResolveCardPlay(ActionResult result, CardData playedCard = null)
        {
            if (result != ActionResult.Success) { _hint.text = "No se pudo jugar la carta"; return; }
            if (playedCard != null)
                AudioManager.Instance?.PlaySfx(Sfx(playedCard.playSoundId, AudioId.CardPlay));
            AfterAction();
        }

        // La carta se captura ANTES de PlayCard porque jugarla repone la mano en ese índice.
        private void OnDeploySlotChosen(int slot)
        {
            CardData played = _engine.ActivePlayer.hand[_pendingCard];
            ResolveCardPlay(_engine.PlayCard(_pendingCard, deploySlot: slot), played);
        }

        private void OnEffectTargetChosen(int slot)
        {
            CardData played = _engine.ActivePlayer.hand[_pendingCard];
            ResolveCardPlay(_engine.PlayCard(_pendingCard, effectTargetSlot: slot), played);
        }

        /// <summary>Id específico si está seteado; si no, el default global. Punto único de fallback de SFX.</summary>
        private static string Sfx(string specific, string fallback) =>
            string.IsNullOrEmpty(specific) ? fallback : specific;

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

            // worldBound está en panel space; WorldToLocal convierte al content space del root,
            // que es el origin correcto para position:absolute en UI Toolkit.
            Rect wb = slotEl.worldBound;
            Vector2 local = _root.WorldToLocal(wb.position);
            float slotW = wb.width;
            _popover.style.left = local.x;
            _popover.style.top = Mathf.Max(0f, local.y - 44f);
            _popover.style.display = DisplayStyle.Flex;

            // Tras el layout ya conocemos el ancho real del popover: lo centramos sobre
            // el slot y lo clampeamos para que no se salga por los bordes (slots del borde derecho).
            _popover.schedule.Execute(() =>
            {
                float w = _popover.resolvedStyle.width;
                float avail = _root.contentRect.width;
                float centered = local.x + slotW * 0.5f - w * 0.5f;
                float maxLeft = Mathf.Max(4f, avail - w - 4f);
                _popover.style.left = Mathf.Clamp(centered, 4f, maxLeft);
            }).StartingIn(0);
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

            PrepareAttackAnimation(_pendingAttacker);
            ResolveAttack(_engine.AttackWithUnit(_pendingAttacker));
        }

        private void OnAttackTargetChosen(int slot)
        {
            PrepareAttackAnimation(_pendingAttacker);
            ResolveAttack(_engine.AttackWithUnit(_pendingAttacker, new[] { slot }));
        }

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
            RenderTurnChip();
            _hint.text = string.Empty;
            _endTurnButton.text = "Terminar turno";
            RenderPanel(0);
            RenderPanel(1);
            RenderSlots();
            RenderHand();
            UpdateZones();
            if (_animAttackerIdx >= 0) ApplyPendingAnimations();
        }

        /// <summary>Actualiza el chip de turno y lo manda al lado del jugador activo.</summary>
        private void RenderTurnChip()
        {
            bool p0Active = _engine.ActiveIndex == 0;
            _turnChip.text = $"▶ {Display(_engine.ActivePlayer.faction)}";
            _turnChip.EnableInClassList("turn-chip--left", p0Active);
            _turnChip.EnableInClassList("turn-chip--right", !p0Active);
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
            VisualElement[] slotEls = playerIndex == 0 ? _p0SlotEls : _p1SlotEls;
            PlayerState p = _engine.PlayerAt(playerIndex);
            int activeIdx = _engine.ActiveIndex;

            for (int i = 0; i < p.unitSlots.Length; i++)
            {
                UnitSlot slot = p.unitSlots[i];
                var el = new VisualElement();
                el.AddToClassList("slot");
                slotEls[i] = el;

                if (slot == null)
                {
                    el.AddToClassList("slot--empty");
                    el.Add(new Label("—"));
                }
                else
                {
                    var nameLabel = new Label(slot.unit.cardName);
                    nameLabel.AddToClassList("slot__name");
                    var hpLabel = new Label($"HP {slot.currentHp}/{slot.MaxHp}");
                    hpLabel.AddToClassList("slot__meta");
                    el.Add(nameLabel);
                    el.Add(hpLabel);

                    float ratio = Mathf.Clamp01((float)slot.currentHp / slot.MaxHp);
                    var barOuter = new VisualElement();
                    barOuter.AddToClassList("slot__hp-bar-outer");
                    var barInner = new VisualElement();
                    barInner.AddToClassList("slot__hp-bar-inner");
                    barInner.AddToClassList(ratio > 0.5f ? "hp-bar--high" : ratio > 0.25f ? "hp-bar--mid" : "hp-bar--low");
                    barInner.style.width = Length.Percent(ratio * 100f);
                    barOuter.Add(barInner);
                    el.Add(barOuter);
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
                var el = BuildCardVisual(card);
                if (!active.CanAfford(card)) el.AddToClassList("card--unaffordable");
                if (cardUsed) el.AddToClassList("card--used");

                if (!cardUsed) MakeDraggable(el, i);
                _hand.Add(el);
            }
        }

        /// <summary>
        /// Construye el visual de una carta (.card con nombre/costo/cuerpo).
        /// Lo comparten la mano y el ghost que acompaña el drag.
        /// </summary>
        private static VisualElement BuildCardVisual(CardData card)
        {
            var el = new VisualElement();
            el.AddToClassList("card");
            var name = new Label(card.cardName); name.AddToClassList("card__name");
            var cost = new Label(CostText(card)); cost.AddToClassList("card__cost");
            var body = new Label(EffectText(card)); body.AddToClassList("card__body");
            el.Add(name); el.Add(cost); el.Add(body);
            return el;
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

            // El ghost es una copia visual de la carta (no solo el nombre) que sigue al puntero.
            _ghost = BuildCardVisual(_engine.ActivePlayer.hand[index]);
            _ghost.AddToClassList("drag-ghost");
            _ghost.pickingMode = PickingMode.Ignore;
            // Forzamos absolute inline: si la regla USS no se resuelve a tiempo, el ghost
            // entraría en el flujo del root (flex column) y empujaría la banda inferior.
            _ghost.style.position = Position.Absolute;
            _root.Add(_ghost);

            // Offset en content space: mantiene el ghost alineado al punto de agarre
            Vector2 cardLocal = _root.WorldToLocal(card.worldBound.position);
            Vector2 pointerLocal = _root.WorldToLocal(e.position);
            _dragPointerOffset = cardLocal - pointerLocal;

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
            Vector2 local = _root.WorldToLocal(pos);
            _ghost.style.left = local.x + _dragPointerOffset.x;
            _ghost.style.top = local.y + _dragPointerOffset.y;
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

        // ── Animaciones de combate ───────────────────────────────────────────────

        private void PrepareAttackAnimation(int attackerSlot)
        {
            _animAttackerIdx = attackerSlot;
            _animAttackerPlayer = _engine.ActiveIndex;
            _animHitSoundId = _engine.ActivePlayer.unitSlots[attackerSlot]?.unit.attack.hitSoundId;
            int opp = 1 - _engine.ActiveIndex;
            var p = _engine.PlayerAt(opp);
            _animOpponentSnap = new int[p.unitSlots.Length];
            for (int i = 0; i < _animOpponentSnap.Length; i++)
                _animOpponentSnap[i] = p.unitSlots[i]?.currentHp ?? 0;
        }

        private void ApplyPendingAnimations()
        {
            VisualElement[] attackerEls = _animAttackerPlayer == 0 ? _p0SlotEls : _p1SlotEls;
            VisualElement[] defenderEls = _animAttackerPlayer == 0 ? _p1SlotEls : _p0SlotEls;
            int oppIdx = 1 - _animAttackerPlayer;

            FlashElement(attackerEls[_animAttackerIdx], "slot--flash-attack");

            PlayerState opp = _engine.PlayerAt(oppIdx);
            bool anyHit = false;
            for (int i = 0; i < _animOpponentSnap.Length; i++)
            {
                int hpBefore = _animOpponentSnap[i];
                if (hpBefore <= 0) continue;
                int hpNow = opp.unitSlots[i]?.currentHp ?? 0;
                if (hpBefore <= hpNow) continue;

                anyHit = true;
                bool killed = hpNow <= 0;
                FlashElement(defenderEls[i], killed ? "slot--flash-dead" : "slot--flash-hit");
                ShakeElement(defenderEls[i]);
            }

            // Un solo golpe por ataque cuando efectivamente impacta (un whiff a slot vacío no suena).
            if (anyHit)
                AudioManager.Instance?.PlaySfx(Sfx(_animHitSoundId, AudioId.AttackHit));

            _animAttackerIdx = -1;
            _animAttackerPlayer = -1;
            _animOpponentSnap = null;
            _animHitSoundId = null;
        }

        private static void FlashElement(VisualElement el, string cls)
        {
            el.AddToClassList(cls);
            el.schedule.Execute(() => el.RemoveFromClassList(cls)).StartingIn(500);
        }

        private static void ShakeElement(VisualElement el)
        {
            float[] xs = { 7f, -6f, 5f, -4f, 3f, -2f, 0f };
            int step = 0;
            el.schedule.Execute(() =>
            {
                el.transform.position = step < xs.Length
                    ? new Vector3(xs[step], 0f, 0f)
                    : Vector3.zero;
                step++;
            }).Every(28).Until(() => step > xs.Length);
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

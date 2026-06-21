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

        private enum Mode { Acting, AwaitDeploySlot, AwaitEffectTarget, AwaitSecondSlot, AwaitAttackTarget, Finished }

        private GameEngine _engine;
        private Mode _mode = Mode.Acting;
        private int _pendingCard = -1;
        private int _pendingAttacker = -1;
        private int _pendingFirstSlot = -1;   // primer slot elegido para efectos de dos slots (Move/Swap)
        private TargetType _pendingEffectSide;

        // Drag & drop
        private bool _dragging;
        private int _dragIndex = -1;
        private VisualElement _ghost;
        private Vector2 _dragPointerOffset;
        private int _dragTargetSide = -1;                              // lado cuyos slots aceptan la carta
        private readonly List<int> _dragEligibleSlots = new List<int>(); // índices de slot válidos (en _dragTargetSide)

        // Slot element refs (rebuilt each Render — used for animations)
        private readonly VisualElement[] _p0SlotEls = new VisualElement[6];
        private readonly VisualElement[] _p1SlotEls = new VisualElement[6];

        // Pending attack animation state
        private int _animAttackerIdx = -1;
        private int _animAttackerPlayer = -1;
        private int[] _animOpponentSnap;
        private string _animHitSoundId;

        // UI refs
        private VisualElement _root, _screen, _hand, _p0Slots, _p1Slots, _overlay, _playZone, _discardZone;
        private Button _popover;
        private VisualElement _infoPopover, _infoBody;
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
            BuildInfoPopover();

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
            // El UXML cuelga de un elemento ".screen--game" (name="root"), y los <Style> del UXML
            // están en ÉSE elemento, no en rootVisualElement. Los popovers/ghost se agregan a
            // rootVisualElement (para quedar sobre todo), así que NO heredan Game.uss salvo que les
            // copiemos las stylesheets de la pantalla (ver Stylize). Sin esto se ven sin estilo.
            _screen = root.Q<VisualElement>("root") ?? root;

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
            Stylize(_popover);
        }

        /// <summary>Popover informativo que se despliega al hacer hover sobre una unidad o carta (spec §11.3).</summary>
        private void BuildInfoPopover()
        {
            _infoPopover = new VisualElement();
            _infoPopover.AddToClassList("info-popover");
            _infoPopover.pickingMode = PickingMode.Ignore;  // no roba el hover del slot/carta
            _infoPopover.style.position = Position.Absolute;
            _infoPopover.style.display = DisplayStyle.None;
            _root.Add(_infoPopover);
            Stylize(_infoPopover);
        }

        /// <summary>Copia las stylesheets de la pantalla a un elemento agregado fuera de su subárbol,
        /// para que las clases de Game.uss/Common.uss apliquen (de lo contrario se ve sin estilo).</summary>
        private void Stylize(VisualElement el)
        {
            if (_screen == null) return;
            for (int i = 0; i < _screen.styleSheets.count; i++)
                el.styleSheets.Add(_screen.styleSheets[i]);
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
            _pendingFirstSlot = -1;
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
            // Efectos de dos slots (MoveUnit/SwapUnits): este es el primer slot; falta el segundo.
            if (CardNeedsSecondSlot(_engine.ActivePlayer.hand[_pendingCard]))
            {
                _pendingFirstSlot = slot;
                EnterAwaitSecondSlot();
                return;
            }
            CardData played = _engine.ActivePlayer.hand[_pendingCard];
            ResolveCardPlay(_engine.PlayCard(_pendingCard, effectTargetSlot: slot), played);
        }

        /// <summary>Pide el segundo slot para un efecto de dos slots (Move destino / Swap segundo).</summary>
        private void EnterAwaitSecondSlot()
        {
            _mode = Mode.AwaitSecondSlot;
            CardEffect e = FirstUnitTargetEffect(_engine.ActivePlayer.hand[_pendingCard]);
            _hint.text = e != null && e.effectType == CardEffectType.MoveUnit
                ? "Elegí el slot destino (libre)"
                : "Elegí la segunda unidad a intercambiar";
            _endTurnButton.text = "Cancelar";
            HidePopover();
            RenderSlots();
        }

        private void OnSecondSlotChosen(int slot)
        {
            CardData played = _engine.ActivePlayer.hand[_pendingCard];
            ResolveCardPlay(
                _engine.PlayCard(_pendingCard, effectTargetSlot: _pendingFirstSlot, effectTargetSlotB: slot),
                played);
            _pendingFirstSlot = -1;
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
            HideInfoPopover();
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
            HideInfoPopover();
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

                    AddStatusBadges(el, slot);

                    // Hover → popover informativo (alcance, pasivas, estados, descripción).
                    int capturedHover = i;
                    el.RegisterCallback<PointerEnterEvent>(_ => ShowInfoPopover(capturedHover, playerIndex, el));
                    el.RegisterCallback<PointerLeaveEvent>(_ => HideInfoPopover());
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
                    // "Puede actuar": unidad propia, no se atacó aún este turno, la regla del turno 1
                    // lo permite, y no está aturdida. Los healers también "actúan" (curan).
                    if (playerIndex == activeIdx && slot != null && !_engine.AttackUsed
                        && _engine.CanAttackThisTurn && !slot.IsStunned)
                    {
                        el.AddToClassList("slot--can-act");
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

                case Mode.AwaitSecondSlot:
                    if (playerIndex == activeIdx && slotIndex == _pendingFirstSlot)
                        el.AddToClassList("slot--selected");   // primer slot ya elegido
                    else if (IsValidSecondSlot(playerIndex, slotIndex))
                    {
                        el.AddToClassList("slot--target");
                        el.RegisterCallback<ClickEvent>(_ => OnSecondSlotChosen(captured));
                    }
                    break;
            }
        }

        /// <summary>Validez del segundo slot según el efecto pendiente (Move destino / Swap segundo).</summary>
        private bool IsValidSecondSlot(int playerIndex, int slotIndex)
        {
            CardEffect e = FirstUnitTargetEffect(_engine.ActivePlayer.hand[_pendingCard]);
            if (e == null) return false;
            int active = _engine.ActiveIndex;

            if (e.effectType == CardEffectType.MoveUnit)
            {
                if (playerIndex != active || slotIndex == _pendingFirstSlot) return false;
                UnitSlot mover = _engine.ActivePlayer.unitSlots[_pendingFirstSlot];
                return _engine.ActivePlayer.unitSlots[slotIndex] == null
                       && mover != null && mover.unit.AllowsSlot(slotIndex);  // destino libre y permitido
            }
            // SwapUnits: la segunda unidad enemiga (cualquier slot del rival distinto del primero).
            int side = e.target == TargetType.Self ? active : 1 - active;
            return playerIndex == side && slotIndex != _pendingFirstSlot;
        }

        // ── Badges de estado/equipo por unidad (spec §11.3) ─────────────────────

        private static void AddStatusBadges(VisualElement slotEl, UnitSlot slot)
        {
            if (slot.activeStatuses.Count == 0 && slot.attachedEquipment.Count == 0) return;

            var badges = new VisualElement();
            badges.AddToClassList("slot__badges");

            foreach (StatusEffect s in slot.activeStatuses)
            {
                (string text, string cls, string tip) = StatusBadge(s);
                if (text == null) continue;  // estado de jugador: no debería vivir en una unidad
                badges.Add(MakeBadge(text, cls, tip));
            }

            foreach (EquipmentCardData eq in slot.attachedEquipment)
                badges.Add(MakeBadge(EquipBadgeText(eq), "badge--equip", EquipmentText(eq)));

            slotEl.Add(badges);
        }

        private static VisualElement MakeBadge(string text, string cls, string tooltip)
        {
            var b = new Label(text);
            b.AddToClassList("badge");
            b.AddToClassList(cls);
            b.tooltip = tooltip;
            return b;
        }

        // ── Popover informativo (hover) ─────────────────────────────────────────

        // Hover sobre una unidad desplegada.
        private void ShowInfoPopover(int slotIndex, int playerIndex, VisualElement slotEl)
        {
            if (_dragging) return;
            PlayerState owner = _engine.PlayerAt(playerIndex);
            UnitSlot slot = owner.unitSlots[slotIndex];
            if (slot == null) return;

            BeginInfoPanel(slot.unit.cardName, $"HP {slot.currentHp}/{slot.MaxHp}");
            AddInfoDesc(!string.IsNullOrEmpty(slot.unit.flavorText) ? slot.unit.flavorText : slot.unit.descriptionText);

            AddInfoSection("Alcance");
            AddInfoLine(ReachText(owner, slotIndex));

            var passives = new List<PassiveEffect>(slot.AllPassives());
            if (passives.Count > 0)
            {
                AddInfoSection("Pasivas");
                foreach (PassiveEffect pe in passives) AddInfoLine(PassiveText(pe));
            }

            if (slot.activeStatuses.Count > 0)
            {
                AddInfoSection("Efectos activos");
                foreach (StatusEffect s in slot.activeStatuses)
                {
                    (_, _, string tip) = StatusBadge(s);
                    if (tip != null) AddInfoLine(tip);
                }
            }

            if (slot.attachedEquipment.Count > 0)
            {
                AddInfoSection("Equipo");
                foreach (EquipmentCardData eq in slot.attachedEquipment) AddInfoLine(EquipmentText(eq));
            }

            PositionInfoPopover(slotEl);
        }

        // Hover sobre una carta de la mano (spec §11.5: anatomía de la carta + detalle).
        private void ShowCardInfo(int handIndex, VisualElement cardEl)
        {
            if (_dragging) return;
            if (handIndex < 0 || handIndex >= _engine.ActivePlayer.hand.Count) return;
            CardData card = _engine.ActivePlayer.hand[handIndex];

            BeginInfoPanel(card.cardName, $"{CostText(card)}  ·  {TypeLabel(card)}");
            AddInfoDesc(!string.IsNullOrEmpty(card.flavorText) ? card.flavorText : card.descriptionText);

            if (card is UnitCardData u)
            {
                AddInfoSection("Unidad");
                AddInfoLine($"HP {u.maxHp}");
                AddInfoLine(AttackInfoText(u.attack));
                AddInfoLine($"Deploy: {DeployZoneText(u.allowedSlots)}");
                if (u.passiveEffects.Count > 0)
                {
                    AddInfoSection("Pasivas");
                    foreach (PassiveEffect pe in u.passiveEffects) AddInfoLine(PassiveText(pe));
                }
            }
            else if (card is ActionCardData a)
            {
                AddInfoSection("Efecto");
                foreach (CardEffect ef in a.effects) AddInfoLine(EffectPart(ef));
            }
            else if (card is EquipmentCardData eq)
            {
                AddInfoSection("Equipo");
                foreach (StatModifier m in eq.statModifiers)
                    AddInfoLine($"+{m.value} {(m.stat == StatType.MaxHp ? "HP máx" : "daño")}");
                foreach (PassiveEffect pe in eq.grantedPassives) AddInfoLine(PassiveText(pe));
            }

            PositionInfoPopover(cardEl);
        }

        /// <summary>Arma el header (título + subtítulo) y el cuerpo del popover informativo.</summary>
        private void BeginInfoPanel(string title, string subtitle)
        {
            _infoPopover.Clear();

            var header = new VisualElement();
            header.AddToClassList("info-popover__header");
            var t = new Label(title);
            t.AddToClassList("info-popover__title");
            header.Add(t);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var s = new Label(subtitle);
                s.AddToClassList("info-popover__hp");
                header.Add(s);
            }
            _infoPopover.Add(header);

            _infoBody = new VisualElement();
            _infoBody.AddToClassList("info-popover__body");
            _infoPopover.Add(_infoBody);
        }

        private void AddInfoDesc(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return;
            var d = new Label(desc);
            d.AddToClassList("info-popover__desc");
            _infoBody.Add(d);
        }

        private void AddInfoSection(string title)
        {
            var s = new Label(title);
            s.AddToClassList("info-popover__section");
            _infoBody.Add(s);
        }

        private void AddInfoLine(string text)
        {
            var l = new Label(text);
            l.AddToClassList("info-popover__line");
            _infoBody.Add(l);
        }

        private void PositionInfoPopover(VisualElement slotEl)
        {
            Rect wb = slotEl.worldBound;
            Vector2 local = _root.WorldToLocal(wb.position);
            float slotW = wb.width;
            _infoPopover.style.left = local.x;
            _infoPopover.style.top = 0f;  // provisional; se reubica tras el layout
            _infoPopover.style.display = DisplayStyle.Flex;

            // Tras el layout conocemos el tamaño real: lo centramos sobre el slot y lo colocamos
            // ARRIBA del slot (la banda de slots está abajo), clampeado dentro del root.
            _infoPopover.schedule.Execute(() =>
            {
                float w = _infoPopover.resolvedStyle.width;
                float h = _infoPopover.resolvedStyle.height;
                float avail = _root.contentRect.width;
                float centered = local.x + slotW * 0.5f - w * 0.5f;
                float maxLeft = Mathf.Max(4f, avail - w - 4f);
                _infoPopover.style.left = Mathf.Clamp(centered, 4f, maxLeft);
                _infoPopover.style.top = Mathf.Max(4f, local.y - h - 8f);
            }).StartingIn(0);
        }

        private void HideInfoPopover()
        {
            if (_infoPopover != null) _infoPopover.style.display = DisplayStyle.None;
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

                int captured = i;  // hover → popover informativo de la carta
                el.RegisterCallback<PointerEnterEvent>(_ => ShowCardInfo(captured, el));
                el.RegisterCallback<PointerLeaveEvent>(_ => HideInfoPopover());

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
            HideInfoPopover();

            // El ghost es una copia visual de la carta (no solo el nombre) que sigue al puntero.
            _ghost = BuildCardVisual(_engine.ActivePlayer.hand[index]);
            _ghost.AddToClassList("drag-ghost");
            _ghost.pickingMode = PickingMode.Ignore;
            Stylize(_ghost);
            // Forzamos absolute inline: si la regla USS no se resuelve a tiempo, el ghost
            // entraría en el flujo del root (flex column) y empujaría la banda inferior.
            _ghost.style.position = Position.Absolute;
            _root.Add(_ghost);

            // Offset en content space: mantiene el ghost alineado al punto de agarre
            Vector2 cardLocal = _root.WorldToLocal(card.worldBound.position);
            Vector2 pointerLocal = _root.WorldToLocal(e.position);
            _dragPointerOffset = cardLocal - pointerLocal;

            // Iluminá los slots que pueden recibir esta carta.
            HighlightDropTargets(_engine.ActivePlayer.hand[index]);

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
            ClearDropHighlights();

            // Orden de drop targets: slot válido → JUGAR → DESCARTAR → (cualquier otro lado: se cancela).
            // Soltar sobre un slot inválido o fuera de toda zona NO juega la carta ni gasta recursos:
            // OnDragEnd no llama a PlayCard, así que la carta queda intacta en la mano.
            Vector2 pos = e.position;
            int slot = SlotUnderPointer(pos);
            if (slot >= 0) DropOnSlot(index, slot);
            else if (_playZone.worldBound.Contains(pos)) TryPlayCard(index);
            else if (_discardZone.worldBound.Contains(pos)) DoDiscard(index);
            // else: drop inválido → no se hace nada (la carta vuelve a su lugar al no re-renderizar).
        }

        // ── Drop sobre slot (carta arrastrada directo a su objetivo) ─────────────

        /// <summary>Calcula los slots que pueden recibir <paramref name="card"/> y los resalta.</summary>
        private void HighlightDropTargets(CardData card)
        {
            _dragEligibleSlots.Clear();
            _dragTargetSide = -1;

            int active = _engine.ActiveIndex;

            if (card is UnitCardData unit)
            {
                _dragTargetSide = active;  // sólo slots propios permitidos y LIBRES (no hay reemplazo, §8.3)
                UnitSlot[] slots = _engine.PlayerAt(active).unitSlots;
                for (int i = 0; i < slots.Length; i++)
                    if (unit.AllowsSlot(i) && slots[i] == null) _dragEligibleSlots.Add(i);
            }
            else if (card is EquipmentCardData)
            {
                _dragTargetSide = active;  // se equipa sobre una unidad propia
                AddOccupiedSlots(active);
            }
            else if (card is ActionCardData action)
            {
                // Primer efecto que apunta a una unidad. Para Move/Swap éste es el PRIMER slot;
                // el segundo se pide después (AwaitSecondSlot).
                foreach (CardEffect ef in action.effects)
                {
                    if (!ef.TargetsAUnitSlot || ef.targetSlot >= 0) continue;
                    _dragTargetSide = ef.target == TargetType.Self ? active : 1 - active;
                    AddOccupiedSlots(_dragTargetSide);
                    break;
                }
            }

            if (_dragTargetSide < 0) return;
            VisualElement[] els = _dragTargetSide == 0 ? _p0SlotEls : _p1SlotEls;
            foreach (int i in _dragEligibleSlots)
                els[i]?.AddToClassList("slot--drop-ok");
        }

        private void AddOccupiedSlots(int playerIndex)
        {
            UnitSlot[] slots = _engine.PlayerAt(playerIndex).unitSlots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null) _dragEligibleSlots.Add(i);
        }

        private void ClearDropHighlights()
        {
            foreach (VisualElement el in _p0SlotEls) el?.RemoveFromClassList("slot--drop-ok");
            foreach (VisualElement el in _p1SlotEls) el?.RemoveFromClassList("slot--drop-ok");
        }

        /// <summary>Índice del slot ELEGIBLE bajo el puntero (en _dragTargetSide); -1 si no hay.</summary>
        private int SlotUnderPointer(Vector2 pos)
        {
            if (_dragTargetSide < 0) return -1;
            VisualElement[] els = _dragTargetSide == 0 ? _p0SlotEls : _p1SlotEls;
            foreach (int i in _dragEligibleSlots)
                if (els[i] != null && els[i].worldBound.Contains(pos)) return i;
            return -1;
        }

        private void DropOnSlot(int handIndex, int slot)
        {
            if (!_engine.CanAfford(handIndex)) { _hint.text = "No te alcanzan los recursos"; return; }

            CardData card = _engine.ActivePlayer.hand[handIndex];

            // Move/Swap: el slot soltado es el PRIMERO; pedimos el segundo por click.
            if (CardNeedsSecondSlot(card))
            {
                _pendingCard = handIndex;
                _pendingFirstSlot = slot;
                EnterAwaitSecondSlot();
                return;
            }

            ActionResult result = card is UnitCardData
                ? _engine.PlayCard(handIndex, deploySlot: slot)
                : _engine.PlayCard(handIndex, effectTargetSlot: slot);   // equipo + acción de 1 objetivo
            ResolveCardPlay(result, card);
        }

        /// <summary>Primer efecto de la carta que apunta a un slot de unidad y lo elige el jugador.</summary>
        private static CardEffect FirstUnitTargetEffect(CardData card)
        {
            if (card is ActionCardData a)
                foreach (CardEffect e in a.effects)
                    if (e.TargetsAUnitSlot && e.targetSlot < 0)
                        return e;
            return null;
        }

        /// <summary>True si la carta necesita un segundo slot (MoveUnit destino / SwapUnits segundo).</summary>
        private static bool CardNeedsSecondSlot(CardData card)
        {
            CardEffect e = FirstUnitTargetEffect(card);
            return e != null && e.NeedsSecondSlot;
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
                    return ApplyStatusText(e.status, e.target);
                case CardEffectType.MoveUnit:
                    return "Mueve una unidad propia a un slot libre";
                case CardEffectType.SwapUnits:
                    return "Intercambia dos unidades enemigas de slot";
                default:
                    return "";
            }
        }

        /// <summary>Descripción legible de un ApplyStatus (estado de jugador o por unidad).</summary>
        private static string ApplyStatusText(StatusEffect s, TargetType target)
        {
            if (s == null) return "";
            string who = target == TargetType.Self ? "propia" : "enemiga";
            return s.statusType switch
            {
                StatusType.SkipProduction => "El rival saltea su próxima producción",
                StatusType.DoubleProduction => "Duplicás tu próxima producción",
                StatusType.Stun => $"Aturde {s.counter} turno(s) a una unidad {who}",
                StatusType.Poison => $"Veneno ({s.value}/turno, {s.counter}t) a una unidad {who}",
                StatusType.Furia => $"Furia (+{s.value} daño, {s.counter}t) a una unidad {who}",
                StatusType.Desmoralizar => $"Desmoraliza (−{s.value} daño, {s.counter}t) a una unidad {who}",
                _ => ""
            };
        }

        /// <summary>Texto del alcance del ataque/cura de una unidad: a qué slots llega y cuánto.</summary>
        private string ReachText(PlayerState owner, int slotIndex)
        {
            UnitSlot slot = owner.unitSlots[slotIndex];
            UnitAttack ua = slot.unit.attack;
            List<int> idxs = GameEngine.ResolveSlots(ua.reference, ua.pattern, slotIndex);
            idxs.Sort();

            string nums;
            if (idxs.Count == 0) nums = "ninguno";
            else
            {
                var parts = new List<string>();
                foreach (int idx in idxs) parts.Add((idx + 1).ToString());  // 1-based para el usuario
                nums = string.Join(", ", parts);
            }

            string pick = ua.RequiresChoice ? $"elegí {ua.pickCount} de slots " : "slots ";
            if (ua.IsHeal)
                return $"Cura {ua.damagePerSlot} HP · {pick}propios {nums}";

            int dmg = _engine.EffectiveAttackDamage(owner.unitSlots, slotIndex);
            return $"Pega {dmg} · {pick}enemigos {nums}";
        }

        /// <summary>Descripción legible de una pasiva (para el popover de info).</summary>
        private static string PassiveText(PassiveEffect pe) => pe.passiveType switch
        {
            PassiveType.ProduceResource => $"Produce +{pe.value}{ResSym(pe.resource)} por turno",
            PassiveType.Regeneration => $"Regenera {pe.value} HP por turno",
            PassiveType.AuraDamage => $"Aura: +{pe.value} daño a aliadas adyacentes",
            PassiveType.Retaliate => $"Espinas: devuelve {pe.value} de daño al atacante",
            PassiveType.TurnDamage => $"Daña {pe.value} a la vanguardia enemiga por turno",
            PassiveType.TurnStatus => $"Aplica {UnitStatusName(pe.status)} a la vanguardia enemiga por turno",
            _ => ""
        };

        /// <summary>(texto del badge, clase USS, tooltip) de un estado por unidad. null = estado de jugador.</summary>
        private static (string, string, string) StatusBadge(StatusEffect s) => s.statusType switch
        {
            StatusType.Poison => ($"Ven {s.value}", "badge--poison", $"Veneno: {s.value} daño/turno · {s.counter}t"),
            StatusType.Stun => ("Atur", "badge--stun", $"Aturdido: no puede actuar · {s.counter}t"),
            StatusType.Furia => ($"+{s.value}", "badge--furia", $"Furia: +{s.value} daño · {s.counter}t"),
            StatusType.Desmoralizar => ($"-{s.value}", "badge--desmor", $"Desmoralizado: -{s.value} daño · {s.counter}t"),
            _ => (null, null, null)
        };

        private static string UnitStatusName(StatusEffect s)
        {
            if (s == null) return "estado";
            return s.statusType switch
            {
                StatusType.Poison => $"Veneno ({s.value})",
                StatusType.Stun => "Aturdir",
                StatusType.Furia => $"Furia (+{s.value})",
                StatusType.Desmoralizar => $"Desmoralizar (-{s.value})",
                _ => "estado"
            };
        }

        /// <summary>Texto corto del badge de un equipo (qué aporta principalmente).</summary>
        private static string EquipBadgeText(EquipmentCardData eq)
        {
            foreach (StatModifier m in eq.statModifiers)
                if (m.stat == StatType.MaxHp) return "+HP";
            foreach (StatModifier m in eq.statModifiers)
                if (m.stat == StatType.Damage) return "+ATK";
            return eq.grantedPassives.Count > 0 ? "+P" : "EQ";
        }

        /// <summary>Descripción legible de un equipo: nombre + lo que suma/otorga.</summary>
        private static string EquipmentText(EquipmentCardData eq)
        {
            var parts = new List<string>();
            foreach (StatModifier m in eq.statModifiers)
                parts.Add($"+{m.value} {(m.stat == StatType.MaxHp ? "HP máx" : "daño")}");
            foreach (PassiveEffect p in eq.grantedPassives)
                parts.Add(PassiveText(p));
            string detail = parts.Count > 0 ? string.Join(", ", parts) : "sin efecto";
            return $"{eq.cardName}: {detail}";
        }

        private static string TypeLabel(CardData c) => c switch
        {
            UnitCardData => "Unidad",
            EquipmentCardData => "Equipo",
            ActionCardData a => $"Acción · {a.actionCategory}",
            _ => ""
        };

        /// <summary>Descripción del ataque/cura de una carta de unidad (sin slot concreto, es preview).</summary>
        private static string AttackInfoText(UnitAttack ua)
        {
            string verb = ua.IsHeal ? "Cura" : "Pega";
            string pat;
            if (ua.reference == AttackReference.Absolute)
            {
                var parts = new List<string>();
                foreach (int p in ua.pattern) parts.Add((p + 1).ToString());  // 1-based
                pat = $"slots {string.Join(", ", parts)}";
            }
            else
            {
                var parts = new List<string>();
                foreach (int o in ua.pattern) parts.Add(o > 0 ? $"+{o}" : o.ToString());
                pat = $"relativo [{string.Join(", ", parts)}]";
            }
            string pick = ua.RequiresChoice ? $", elegí {ua.pickCount}" : "";
            return $"{verb} {ua.damagePerSlot} · {pat}{pick}";
        }

        private static string DeployZoneText(int[] allowed)
        {
            if (allowed == null || allowed.Length == 0) return "cualquier slot";
            var parts = new List<string>();
            foreach (int s in allowed) parts.Add((s + 1).ToString());  // 1-based
            return $"slots {string.Join(", ", parts)}";
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

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

        private enum Mode { Acting, AwaitDeploySlot, AwaitEffectTarget, AwaitConfirmGlobal, AwaitSecondSlot, AwaitAttackTarget, Finished }

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
        private Vector2 _dragStartPos;                                 // posición del PointerDown (para distinguir click de drag)
        private const float ClickThreshold = 8f;                       // movimiento máx (px) para tratar el gesto como click
        private int _dragTargetSide = -1;                              // lado cuyos slots aceptan la carta
        private bool _dragGlobal;                                      // carta global: cualquier slot (ambos lados) confirma
        private readonly List<int> _dragEligibleSlots = new List<int>(); // índices de slot válidos (en _dragTargetSide)

        // Mano en abanico (fan). Orden de reparto estable (no usar _hand.Children(): BringToFront lo reordena).
        private readonly List<VisualElement> _handEls = new List<VisualElement>();
        private float[] _handBaseRot = System.Array.Empty<float>();
        private float[] _handBaseBottom = System.Array.Empty<float>();
        private IVisualElementScheduledItem _cardInfoPending;   // popover de info diferido hasta terminar el hover

        // Slot element refs (rebuilt each Render — used for animations)
        private readonly VisualElement[] _p0SlotEls = new VisualElement[6];
        private readonly VisualElement[] _p1SlotEls = new VisualElement[6];

        // Pending attack animation state
        private int _animAttackerIdx = -1;
        private int _animAttackerPlayer = -1;
        private int[] _animOpponentSnap;
        private string _animHitSoundId;

        // UI refs
        private VisualElement _root, _screen, _hand, _p0Slots, _p1Slots, _overlay;
        private Button _popover;
        private VisualElement _infoPopover, _infoHeader, _infoBody, _inflationMeter;
        private Label _turnChip, _hint, _overlayTitle, _overlayMsg, _firstTurnNotice;
        private Label _inflationPct, _inflationSub;
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
            BuildFirstTurnNotice();
            BuildInflationMeter();

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

            // Teclado: Ctrl + 1..6 descarta la carta n (alternativa al Ctrl+Click del mouse).
            _screen.focusable = true;
            _screen.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _screen.Focus();

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
            _endTurnButton = root.Q<Button>("end-turn-button");

            // Recolocar el abanico cuando cambia el tamaño de la franja (resolución / aspect ratio).
            _hand.RegisterCallback<GeometryChangedEvent>(_ => LayoutHand());

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

        /// <summary>Cartel que avisa al primer jugador que no puede atacar en su turno 1 (spec §3/§16).</summary>
        private void BuildFirstTurnNotice()
        {
            _firstTurnNotice = new Label();
            _firstTurnNotice.AddToClassList("first-turn-notice");
            _firstTurnNotice.pickingMode = PickingMode.Ignore;
            _firstTurnNotice.style.position = Position.Absolute;
            _firstTurnNotice.style.display = DisplayStyle.None;
            _root.Add(_firstTurnNotice);
            Stylize(_firstTurnNotice);
        }

        /// <summary>Medidor de inflación: cartel central que aparece cuando la inflación arranca (spec §3/§11).
        /// Muestra título + % grande + multiplicador efectivo (×1.X).</summary>
        private void BuildInflationMeter()
        {
            _inflationMeter = new VisualElement();
            _inflationMeter.AddToClassList("inflation-meter");
            _inflationMeter.pickingMode = PickingMode.Ignore;
            _inflationMeter.style.position = Position.Absolute;
            _inflationMeter.style.display = DisplayStyle.None;

            var title = new Label("▲  I N F L A C I Ó N");
            title.AddToClassList("inflation-meter__title");
            title.pickingMode = PickingMode.Ignore;

            _inflationPct = new Label("+0%");
            _inflationPct.AddToClassList("inflation-meter__pct");
            _inflationPct.pickingMode = PickingMode.Ignore;

            _inflationSub = new Label("las cartas cuestan ×1.0");
            _inflationSub.AddToClassList("inflation-meter__sub");
            _inflationSub.pickingMode = PickingMode.Ignore;

            _inflationMeter.Add(title);
            _inflationMeter.Add(_inflationPct);
            _inflationMeter.Add(_inflationSub);
            _root.Add(_inflationMeter);
            Stylize(_inflationMeter);
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

            // Si todavía podés atacar o jugar una carta, confirmá antes de pasar el turno.
            if (HasUnusedActions()) { ShowEndTurnConfirm(); return; }
            EndTurnConfirmed();
        }

        private void EndTurnConfirmed()
        {
            _engine.EndTurn();
            if (_engine.IsFinished) { ShowOutcome(); return; }
            BeginActiveTurn();
        }

        /// <summary>¿Quedan acciones sin usar este turno (atacar o jugar una carta)?</summary>
        private bool HasUnusedActions() => CanStillAttack() || CanStillPlayCard();

        private bool CanStillPlayCard()
        {
            if (_engine.CardActionUsed) return false;
            for (int i = 0; i < _engine.ActivePlayer.hand.Count; i++)
                if (_engine.CanAfford(i)) return true;
            return false;
        }

        private bool CanStillAttack()
        {
            if (_engine.AttackUsed || !_engine.CanAttackThisTurn) return false;
            foreach (UnitSlot s in _engine.ActivePlayer.unitSlots)
                if (s != null && !s.IsStunned) return true;
            return false;
        }

        /// <summary>Diálogo confirmar/cancelar antes de terminar el turno con acciones pendientes.</summary>
        private void ShowEndTurnConfirm()
        {
            HidePopover();
            bool canAttack = CanStillAttack();
            bool canPlay = CanStillPlayCard();
            string pending = canAttack && canPlay ? "atacar y jugar una carta"
                : canAttack ? "atacar"
                : "jugar una carta";
            _overlayTitle.text = "¿Terminar el turno?";
            _overlayMsg.text = $"Todavía podés {pending}.";
            WireOverlayButton(_overlayPrimary, "Terminar turno", () => { HideOverlay(); EndTurnConfirmed(); });
            WireOverlayButton(_overlaySecondary, "Cancelar", HideOverlay);
            _overlay.style.display = DisplayStyle.Flex;
        }

        private void HideOverlay() => _overlay.style.display = DisplayStyle.None;

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

            // Unidad: elegir un slot propio libre y permitido (sin reemplazo, §8.3).
            if (_engine.ActivePlayer.hand[index] is UnitCardData unit)
            {
                if (!HasFreeAllowedSlot(unit)) { _hint.text = "No hay slot libre para esa unidad"; return; }
                _pendingCard = index;
                _mode = Mode.AwaitDeploySlot;
                _hint.text = "Elegí un slot libre para desplegar";
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

            // Carta global (sin slot/target): pide un segundo click de confirmación sobre el tablero
            // (cualquier slot, todos resaltados), para que jugarla nunca sea un click accidental.
            _pendingCard = index;
            _mode = Mode.AwaitConfirmGlobal;
            _hint.text = "Click en el tablero para confirmar";
            _endTurnButton.text = "Cancelar";
            RenderSlots();
        }

        private void OnConfirmGlobalPlay()
        {
            CardData played = _engine.ActivePlayer.hand[_pendingCard];
            ResolveCardPlay(_engine.PlayCard(_pendingCard), played);
        }

        /// <summary>¿La unidad tiene al menos un slot propio libre y permitido para desplegar?</summary>
        private bool HasFreeAllowedSlot(UnitCardData unit)
        {
            UnitSlot[] slots = _engine.ActivePlayer.unitSlots;
            for (int i = 0; i < slots.Length; i++)
                if (unit.AllowsSlot(i) && slots[i] == null) return true;
            return false;
        }

        private void DoDiscard(int index)
        {
            if (_engine.CardActionUsed) return;
            ResolveCardPlay(_engine.DiscardCard(index));   // descartar no es "jugar": sin sonido de jugada
        }

        /// <summary>Teclado: Escape cancela la carta/ataque armado; Ctrl + 1..6 descarta la carta n.</summary>
        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                if (_mode != Mode.Acting && _mode != Mode.Finished) { CancelTargeting(); e.StopPropagation(); }
                return;
            }
            if (_mode != Mode.Acting || _engine.CardActionUsed || !e.ctrlKey) return;
            int n = DigitIndex(e.keyCode);
            if (n >= 0 && n < _engine.ActivePlayer.hand.Count) { DoDiscard(n); e.StopPropagation(); }
        }

        /// <summary>Mapea KeyCode 1..6 (fila numérica o teclado numérico) a índice 0..5; -1 si no aplica.</summary>
        private static int DigitIndex(KeyCode key)
        {
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha6) return key - KeyCode.Alpha1;
            if (key >= KeyCode.Keypad1 && key <= KeyCode.Keypad6) return key - KeyCode.Keypad1;
            return -1;
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
            string verb = ua.IsHeal ? "Curar" : "Atacar";
            string icon = ua.IsHeal ? "+" : "⚔";
            // Daño EFECTIVO (base + equipo + Furia + Aura − Desmoralizar), igual que el icono del slot.
            // La cura usa el valor base (las auras/Furia son de ataque, no de cura).
            int amount = ua.IsHeal
                ? ua.damagePerSlot
                : _engine.EffectiveAttackDamage(_engine.ActivePlayer.unitSlots, attackerSlot);
            _popover.text = ua.RequiresChoice
                ? $"{icon} {verb} (elegí {ua.count}) · {amount}"
                : $"{icon} {verb} · {amount}";

            // Muestra a qué slots llega ESTE ataque (preview), del lado correcto (propio si cura).
            HighlightReach(attackerSlot, ua);

            // worldBound está en panel space; WorldToLocal convierte al content space del root,
            // que es el origin correcto para position:absolute en UI Toolkit.
            Rect wb = slotEl.worldBound;
            Vector2 local = _root.WorldToLocal(wb.position);
            ClampPopover(_popover, local.x, wb.width, local.y);
        }

        /// <summary>
        /// Centra <paramref name="pop"/> sobre el slot (anchor en coords de _root) ARRIBA de él y lo
        /// clampea dentro del root. Re-aplica en GeometryChanged porque al primer frame el ancho real
        /// puede no estar resuelto (causa del popover que se salía por el borde derecho).
        /// </summary>
        private void ClampPopover(VisualElement pop, float anchorLeft, float anchorWidth, float anchorTop)
        {
            void Apply()
            {
                float w = pop.resolvedStyle.width;
                float h = pop.resolvedStyle.height;
                float availW = _root.contentRect.width;
                float centered = anchorLeft + anchorWidth * 0.5f - w * 0.5f;
                float maxLeft = Mathf.Max(4f, availW - w - 4f);
                pop.style.left = Mathf.Clamp(centered, 4f, maxLeft);
                pop.style.top = Mathf.Max(4f, anchorTop - h - 8f);
            }

            pop.style.left = anchorLeft;
            pop.style.top = Mathf.Max(4f, anchorTop - 44f);
            pop.style.display = DisplayStyle.Flex;
            Apply();  // si la geometría ya estaba resuelta (no dispara GeometryChanged)

            EventCallback<GeometryChangedEvent> cb = null;
            cb = _ => { Apply(); pop.UnregisterCallback(cb); };
            pop.RegisterCallback(cb);
        }

        private void HidePopover()
        {
            _popover.style.display = DisplayStyle.None;
            ClearReachHighlights();
        }

        /// <summary>Resalta (preview) los slots que alcanza el ataque/cura de la unidad.</summary>
        private void HighlightReach(int attackerSlot, UnitAttack ua)
        {
            ClearReachHighlights();
            int side = ua.IsHeal ? _engine.ActiveIndex : 1 - _engine.ActiveIndex;
            VisualElement[] els = side == 0 ? _p0SlotEls : _p1SlotEls;
            UnitSlot[] targetBoard = _engine.PlayerAt(side).unitSlots;
            foreach (int t in GameEngine.ResolveTargets(ua.mode, ua.count, targetBoard, attackerSlot))
                els[t]?.AddToClassList("slot--reach");
        }

        private void ClearReachHighlights()
        {
            foreach (VisualElement el in _p0SlotEls) el?.RemoveFromClassList("slot--reach");
            foreach (VisualElement el in _p1SlotEls) el?.RemoveFromClassList("slot--reach");
        }

        private void OnPopoverClicked()
        {
            if (_pendingAttacker < 0 || _engine.AttackUsed) { HidePopover(); return; }

            if (_engine.AttackRequiresTarget(_pendingAttacker))
            {
                UnitSlot atk = _engine.ActivePlayer.unitSlots[_pendingAttacker];
                bool heal = atk != null && atk.unit.attack.IsHeal;
                _mode = Mode.AwaitAttackTarget;
                HidePopover();
                _hint.text = heal ? "Elegí a qué aliado curar" : "Elegí el slot enemigo a atacar";
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
            if (result != ActionResult.Success)
            {
                // InvalidTarget acá = no había objetivo válido → la acción se cancela sin gastar (spec §6).
                _hint.text = result == ActionResult.InvalidTarget
                    ? "No hay objetivos a alcance" : "Ataque inválido";
                _mode = Mode.Acting;
                Render();
                return;
            }
            _pendingAttacker = -1;
            AfterAction();
        }

        // ── Render ──────────────────────────────────────────────────────────────

        private void Render()
        {
            HidePopover();
            HideInfoPopover();
            RenderTurnChip();
            RenderInflationMeter();
            _hint.text = string.Empty;
            _endTurnButton.text = "Terminar turno";
            RenderPanel(0);
            RenderPanel(1);
            RenderSlots();
            RenderHand();
            if (_animAttackerIdx >= 0) ApplyPendingAnimations();
        }

        /// <summary>Actualiza el chip de turno y lo manda al lado del jugador activo.</summary>
        private void RenderTurnChip()
        {
            bool p0Active = _engine.ActiveIndex == 0;
            _turnChip.text = $"▶ {Display(_engine.ActivePlayer.faction)}";
            _turnChip.EnableInClassList("turn-chip--left", p0Active);
            _turnChip.EnableInClassList("turn-chip--right", !p0Active);

            // Aviso al primer jugador: en su turno 1 no puede atacar (regla de iniciativa, spec §3/§16).
            // Sólo es true para el primer jugador en HalfTurn 1, así que va de su lado (el activo).
            bool noAttack = _engine.HalfTurn == 1 && !_engine.CanAttackThisTurn;
            _firstTurnNotice.style.display = noAttack ? DisplayStyle.Flex : DisplayStyle.None;
            if (noAttack)
            {
                _firstTurnNotice.text = "No podés atacar en tu primer turno";
                _firstTurnNotice.EnableInClassList("first-turn-notice--left", p0Active);
                _firstTurnNotice.EnableInClassList("first-turn-notice--right", !p0Active);
            }
        }

        /// <summary>Muestra el medidor de inflación cuando está activa (spec §3). El color y el
        /// tamaño escalan por tramos a medida que crece el %.</summary>
        private void RenderInflationMeter()
        {
            if (_inflationMeter == null) return;
            int pct = _engine.InflationPercent;
            if (pct <= 0)
            {
                _inflationMeter.style.display = DisplayStyle.None;
                return;
            }
            _inflationMeter.style.display = DisplayStyle.Flex;
            _inflationPct.text = $"+{pct}%";
            // multiplicador efectivo de costo (lo que se suma a cada carta): ×1.X
            _inflationSub.text = $"las cartas cuestan ×{(1f + pct / 100f):0.0#}";
            // ramp de color en tres tramos: leve (<25%) → medio (25–59%) → alto (≥60%).
            _inflationMeter.EnableInClassList("inflation-meter--mid", pct >= 25 && pct < 60);
            _inflationMeter.EnableInClassList("inflation-meter--high", pct >= 60);
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
                    // Anatomía tipo "carta de unidad" (spec §11.3): arte → barra de HP con número → iconos.
                    // El área de arte deja el hueco del sprite preparado: hoy es un panel tinte-facción con
                    // el nombre; cuando exista el sprite (CardData.sprite) se pinta de fondo (dev-guide §5.1).
                    var art = new VisualElement();
                    art.AddToClassList("slot__art");
                    art.AddToClassList(playerIndex == 0 ? "slot__art--manif" : "slot__art--polis");
                    // Capa propia para el sprite: así el espejado (mirar al rival) no voltea el nombre.
                    // El arte se autoría "mirando a la derecha"; el lado derecho del tablero mira a la
                    // izquierda → se espeja (dev-guide §5.1).
                    var sprite = new VisualElement();
                    sprite.AddToClassList("slot__sprite");
                    ApplyUnitArt(sprite, slot.unit, faceLeft: playerIndex == 1);
                    art.Add(sprite);
                    var nameLabel = new Label(slot.unit.cardName);
                    nameLabel.AddToClassList("slot__name");
                    art.Add(nameLabel);
                    el.Add(art);

                    // Barra de HP con el valor de vida superpuesto y centrado (como la referencia).
                    float ratio = Mathf.Clamp01((float)slot.currentHp / slot.MaxHp);
                    var barOuter = new VisualElement();
                    barOuter.AddToClassList("slot__hp-bar-outer");
                    var barInner = new VisualElement();
                    barInner.AddToClassList("slot__hp-bar-inner");
                    barInner.AddToClassList(ratio > 0.5f ? "hp-bar--high" : ratio > 0.25f ? "hp-bar--mid" : "hp-bar--low");
                    barInner.style.width = Length.Percent(ratio * 100f);
                    barOuter.Add(barInner);
                    var hpLabel = new Label($"{slot.currentHp}/{slot.MaxHp}");
                    hpLabel.AddToClassList("slot__hp-label");
                    barOuter.Add(hpLabel);
                    el.Add(barOuter);

                    // Fila de iconos: stats + producción + pasivas + estados + equipo (un único registro).
                    AddSlotIcons(el, p, playerIndex, i);

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
                    // Sólo slots propios LIBRES y permitidos por la unidad (sin reemplazo, §8.3).
                    if (playerIndex == activeIdx && slot == null
                        && _engine.ActivePlayer.hand[_pendingCard] is UnitCardData du && du.AllowsSlot(slotIndex))
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

                case Mode.AwaitConfirmGlobal:
                    // Carta global: cualquier slot (ocupado o vacío, de cualquier lado) confirma la jugada.
                    el.AddToClassList("slot--target");
                    el.RegisterCallback<ClickEvent>(_ => OnConfirmGlobalPlay());
                    break;

                case Mode.AwaitAttackTarget:
                    // El lado objetivo depende del ataque: HealAllies cura el tablero PROPIO,
                    // el resto pega al rival. Sólo se habilitan los slots realmente alcanzables.
                    if (_pendingAttacker >= 0)
                    {
                        UnitSlot atk = _engine.ActivePlayer.unitSlots[_pendingAttacker];
                        if (atk != null)
                        {
                            UnitAttack ua = atk.unit.attack;
                            int targetSide = ua.IsHeal ? activeIdx : 1 - activeIdx;
                            UnitSlot[] targetBoard = _engine.PlayerAt(targetSide).unitSlots;
                            // Sólo slots con objetivo válido (enemigo ocupado / aliado herido) dentro
                            // del modo de targeting: no se puede elegir un slot que whiffearía (spec §6).
                            bool valid = slot != null && (!ua.IsHeal || slot.currentHp < slot.MaxHp);
                            if (playerIndex == targetSide && valid
                                && GameEngine.ResolveTargets(ua.mode, ua.count, targetBoard, _pendingAttacker).Contains(slotIndex))
                            {
                                el.AddToClassList("slot--target");
                                el.RegisterCallback<ClickEvent>(_ => OnAttackTargetChosen(captured));
                            }
                        }
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

        // ── Iconos de stats/estado/pasiva/equipo por unidad (spec §11.3) ─────────
        //
        // Un único registro: el slot se traduce a una List<SlotIcon> (stat de ataque,
        // pasivas, estados, equipo) y se renderiza todo igual. Agregar un StatusType /
        // PassiveType nuevo = un caso en PassiveIcon/StatusIcon (un solo lugar).

        /// <summary>
        /// Descriptor de un icono de slot. La imagen se carga de <c>Resources/Icons/&lt;iconKey&gt;</c>;
        /// si <see cref="iconKey"/> es null se deriva del sufijo de <see cref="cls"/> (slot-icon--atk →
        /// "atk"). Si no hay textura, cae al <see cref="glyph"/> (texto). <see cref="value"/> puede ser
        /// null. <see cref="title"/> = categoría corta para el popover de hover; <see cref="tip"/> = detalle.
        /// </summary>
        private readonly struct SlotIcon
        {
            public readonly string glyph;
            public readonly string value;
            public readonly string cls;
            public readonly string title;
            public readonly string tip;
            public readonly string iconKey;

            public SlotIcon(string glyph, string value, string cls, string title, string tip, string iconKey = null)
            {
                this.glyph = glyph;
                this.value = value;
                this.cls = cls;
                this.title = title;
                this.tip = tip;
                this.iconKey = iconKey;
            }
        }

        // Texturas de iconos cargadas de Resources/Icons (cache por key). Las genera tools/gen_icons.py.
        private readonly Dictionary<string, Texture2D> _iconTexCache = new Dictionary<string, Texture2D>();

        private Texture2D IconTexture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_iconTexCache.TryGetValue(key, out Texture2D tex))
            {
                tex = Resources.Load<Texture2D>("Icons/" + key);
                _iconTexCache[key] = tex;   // cachea también el null (falta) para no recargar
            }
            return tex;
        }

        // Texturas de arte de unidad cargadas de Resources/Units (cache por key, incluye null = falta).
        private readonly Dictionary<string, Texture2D> _unitTexCache = new Dictionary<string, Texture2D>();

        private Texture2D UnitTexture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_unitTexCache.TryGetValue(key, out Texture2D tex))
            {
                tex = Resources.Load<Texture2D>("Units/" + key);
                _unitTexCache[key] = tex;   // cachea también el null (falta) para no recargar
            }
            return tex;
        }

        /// <summary>Clave de arte por facción para el default de <c>Resources/Units/{faccion}-default</c>
        /// ("manifestantes"/"policias", nombre del enum en minúscula).</summary>
        private static string FactionArtKey(Faction faction) => faction.ToString().ToLowerInvariant();

        /// <summary>
        /// Pinta el sprite de una unidad como fondo de su capa de arte. Prioridad (listo para
        /// "cada unidad su propio sprite", dev-guide §5.1):
        ///   1) sprite propio asignado en el asset (<see cref="CardData.sprite"/>),
        ///   2) textura por convención <c>Resources/Units/{id}</c> (sprite propio de esa unidad),
        ///   3) default de facción <c>Resources/Units/{faction}-default</c> (hoy todos los polis comparten uno).
        /// Si no hay arte, la capa queda vacía. <paramref name="faceLeft"/> espeja el sprite en
        /// horizontal: el arte se autoría mirando a la derecha; el lado derecho del tablero mira al rival.
        /// </summary>
        private void ApplyUnitArt(VisualElement sprite, UnitCardData unit, bool faceLeft)
        {
            StyleBackground bg;
            if (unit.sprite != null)
            {
                bg = new StyleBackground(unit.sprite);
            }
            else
            {
                Texture2D tex = UnitTexture(unit.id) ?? UnitTexture(FactionArtKey(unit.faction) + "-default");
                if (tex == null) return;
                bg = new StyleBackground(tex);
            }
            sprite.style.backgroundImage = bg;
            if (faceLeft)
                sprite.style.scale = new Scale(new Vector2(-1f, 1f));
        }

        private void AddSlotIcons(VisualElement slotEl, PlayerState owner, int playerIndex, int slotIndex)
        {
            List<SlotIcon> icons = BuildSlotIcons(owner, slotIndex);
            if (icons.Count == 0) return;

            var row = new VisualElement();
            row.AddToClassList("slot__icons");
            foreach (SlotIcon ic in icons)
            {
                VisualElement iconEl = MakeIcon(ic);
                // Hover por icono: muestra el detalle de ESE efecto; al salir, vuelve el popover
                // completo de la unidad (el slot sigue "hovereado"). StopPropagation evita el flash.
                SlotIcon captured = ic;
                iconEl.RegisterCallback<PointerEnterEvent>(ev => { ev.StopPropagation(); ShowIconInfo(captured, iconEl); });
                iconEl.RegisterCallback<PointerLeaveEvent>(ev => { ev.StopPropagation(); ShowInfoPopover(slotIndex, playerIndex, slotEl); });
                row.Add(iconEl);
            }
            slotEl.Add(row);
        }

        /// <summary>Popover estilizado con el detalle de un solo icono (estado/pasiva/stat/equipo).</summary>
        private void ShowIconInfo(SlotIcon ic, VisualElement anchorEl)
        {
            if (_dragging) return;
            BeginInfoPanel(ic.title, ic.value);
            AddInfoLine(ic.tip);
            PositionInfoPopover(anchorEl);
        }

        /// <summary>Traduce un slot a sus iconos: stat de acción + pasivas + estados + equipo.</summary>
        private List<SlotIcon> BuildSlotIcons(PlayerState owner, int slotIndex)
        {
            UnitSlot slot = owner.unitSlots[slotIndex];
            var icons = new List<SlotIcon>();

            // 1) Stat principal: daño (o cura) efectivo + cuántos objetivos.
            // AoE (mode == All) usa un icono propio (estallido / cruz-en-anillo): la cantidad de
            // objetivos depende de la formación, así que no se muestra ×N — el icono ya dice "a todas".
            UnitAttack ua = slot.unit.attack;
            if (ua != null && ua.damagePerSlot != 0)
            {
                bool aoe = ua.mode == TargetMode.All;
                int targets = AttackTargetCount(ua);  // golpes potenciales (Frontmost/Backmost/Any)
                bool multi = aoe || targets > 1;
                if (ua.IsHeal)
                {
                    string val = (!aoe && targets > 1) ? $"{ua.damagePerSlot}×{targets}" : ua.damagePerSlot.ToString();
                    string tip = multi
                        ? $"Cura {ua.damagePerSlot} a cada una · {AttackShape(ua)}"
                        : $"Cura {ua.damagePerSlot} · {AttackShape(ua)}";
                    icons.Add(new SlotIcon("✚", val, "slot-icon--heal", "Curación", tip, aoe ? "heal-aoe" : null));
                }
                else
                {
                    int dmg = _engine.EffectiveAttackDamage(owner.unitSlots, slotIndex);
                    string val = (!aoe && targets > 1) ? $"{dmg}×{targets}" : dmg.ToString();
                    string tip = multi
                        ? $"Pega {dmg} a cada uno · {AttackShape(ua)}"
                        : $"Pega {dmg} · {AttackShape(ua)}";
                    icons.Add(new SlotIcon("⚔", val, "slot-icon--atk", "Ataque", tip, aoe ? "atk-aoe" : null));
                }
            }

            // 2) Pasivas efectivas (propias + de equipo): producción, regen, aura, espinas, daño/estado por turno.
            foreach (PassiveEffect pe in slot.AllPassives())
                icons.Add(PassiveIcon(pe));

            // 3) Estados activos por unidad (Veneno/Aturdir/Furia/Desmoralizar).
            foreach (StatusEffect s in slot.activeStatuses)
            {
                SlotIcon? ic = StatusIcon(s);
                if (ic.HasValue) icons.Add(ic.Value);
            }

            // 4) Equipo adjunto: señala su presencia (los stats que aporta ya van en HP/ATK; detalle en hover).
            foreach (EquipmentCardData eq in slot.attachedEquipment)
                icons.Add(new SlotIcon("⚙", null, "slot-icon--equip", "Equipo", EquipmentText(eq)));

            return icons;
        }

        private VisualElement MakeIcon(SlotIcon ic)
        {
            var box = new VisualElement();
            box.AddToClassList("slot-icon");
            if (ic.cls != null) box.AddToClassList(ic.cls);

            // Key explícita o derivada del sufijo de la clase (slot-icon--atk → "atk").
            string key = ic.iconKey;
            if (key == null && ic.cls != null && ic.cls.StartsWith("slot-icon--"))
                key = ic.cls.Substring("slot-icon--".Length);
            Texture2D tex = IconTexture(key);

            if (tex != null)
            {
                var img = new VisualElement();
                img.AddToClassList("slot-icon__img");
                img.style.backgroundImage = new StyleBackground(tex);
                box.Add(img);
            }
            else if (!string.IsNullOrEmpty(ic.glyph))   // fallback si falta la textura
            {
                var g = new Label(ic.glyph);
                g.AddToClassList("slot-icon__glyph");
                box.Add(g);
            }

            if (!string.IsNullOrEmpty(ic.value))
            {
                var v = new Label(ic.value);
                v.AddToClassList("slot-icon__value");
                box.Add(v);
            }
            return box;
        }

        /// <summary>Icono de una pasiva (un caso por <see cref="PassiveType"/>).</summary>
        private static SlotIcon PassiveIcon(PassiveEffect pe) => pe.passiveType switch
        {
            PassiveType.ProduceResource => new SlotIcon(ResSym(pe.resource), $"+{pe.value}", "slot-icon--produce", "Producción", PassiveText(pe), $"res-{pe.resource.ToString().ToLowerInvariant()}"),
            PassiveType.Regeneration    => new SlotIcon("♥", $"+{pe.value}", "slot-icon--regen", "Regeneración", PassiveText(pe)),
            PassiveType.AuraDamage      => new SlotIcon("✦", $"+{pe.value}", "slot-icon--aura", "Aura", PassiveText(pe)),
            PassiveType.Retaliate       => new SlotIcon("🛡", pe.value.ToString(), "slot-icon--thorns", "Espinas", PassiveText(pe)),
            PassiveType.TurnDamage      => new SlotIcon("🔥", pe.value.ToString(), "slot-icon--turndmg", "Daño por turno", PassiveText(pe)),
            PassiveType.TurnStatus      => new SlotIcon("☣", null, "slot-icon--turnstatus", "Estado por turno", PassiveText(pe)),
            _                           => new SlotIcon("•", pe.value.ToString(), "slot-icon--passive", "Pasiva", PassiveText(pe)),
        };

        /// <summary>Icono de un estado por unidad. null = estado de jugador (no vive en un slot).</summary>
        private static SlotIcon? StatusIcon(StatusEffect s)
        {
            (string glyph, string value, string cls, string title) = s.statusType switch
            {
                StatusType.Poison       => ("☠", s.value.ToString(), "slot-icon--poison", "Veneno"),
                StatusType.Stun         => ("✸", (string)null, "slot-icon--stun", "Aturdido"),
                StatusType.Furia        => ("↑", $"+{s.value}", "slot-icon--furia", "Furia"),
                StatusType.Desmoralizar => ("↓", $"-{s.value}", "slot-icon--desmor", "Desmoralizado"),
                _                       => ((string)null, (string)null, (string)null, (string)null),
            };
            if (glyph == null) return null;
            (_, _, string tip) = StatusBadge(s);
            return new SlotIcon(glyph, value, cls, title, tip);
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

            BeginInfoPanel(card.cardName, null);
            AddHeaderPills((CostText(card, _engine.InflationPercent), "info-popover__pill--cost"),
                           (TypeLabel(card), "info-popover__pill--type"));
            AddPopoverArt(card);
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
            _infoHeader = header;
            _infoPopover.Add(header);

            _infoBody = new VisualElement();
            _infoBody.AddToClassList("info-popover__body");
            _infoPopover.Add(_infoBody);
        }

        /// <summary>Pills de costo + tipo en el header del popover (en vez del subtítulo en texto).</summary>
        private void AddHeaderPills(params (string text, string variant)[] pills)
        {
            var row = new VisualElement();
            row.AddToClassList("info-popover__pills");
            foreach ((string text, string variant) in pills)
            {
                var l = new Label(text);
                l.AddToClassList("info-popover__pill");
                if (!string.IsNullOrEmpty(variant)) l.AddToClassList(variant);
                row.Add(l);
            }
            _infoHeader.Add(row);
        }

        /// <summary>Miniatura del arte de la carta arriba del cuerpo del popover.</summary>
        private void AddPopoverArt(CardData card)
        {
            var art = new VisualElement();
            art.AddToClassList("info-popover__art");
            Texture2D tex = CardArtTexture(card);
            if (card.sprite != null) art.style.backgroundImage = new StyleBackground(card.sprite);
            else if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
            else return;
            _infoBody.Add(art);
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

        private void PositionInfoPopover(VisualElement anchorEl)
        {
            Rect wb = anchorEl.worldBound;
            Vector2 local = _root.WorldToLocal(wb.position);
            ClampPopover(_infoPopover, local.x, wb.width, local.y);
        }

        private void HideInfoPopover()
        {
            if (_infoPopover != null) _infoPopover.style.display = DisplayStyle.None;
        }

        private void RenderHand()
        {
            _hand.Clear();
            _handEls.Clear();
            PlayerState active = _engine.ActivePlayer;
            bool cardUsed = _engine.CardActionUsed;
            _handBaseRot = new float[active.hand.Count];
            _handBaseBottom = new float[active.hand.Count];

            for (int i = 0; i < active.hand.Count; i++)
            {
                CardData card = active.hand[i];
                var el = BuildCardVisual(card, _engine.InflationPercent);
                if (!active.CanAfford(card, _engine.InflationPercent)) el.AddToClassList("card--unaffordable");
                if (cardUsed) el.AddToClassList("card--used");

                if (!cardUsed) MakeDraggable(el, i);

                int captured = i;  // hover → sube/crece (abanico) + popover informativo de la carta
                el.RegisterCallback<PointerEnterEvent>(_ => OnCardHoverEnter(captured, el));
                el.RegisterCallback<PointerLeaveEvent>(_ => OnCardHoverLeave(captured, el));

                _hand.Add(el);
                _handEls.Add(el);
            }

            LayoutHand();
        }

        // ── Mano en abanico (fan) ────────────────────────────────────────────────

        /// <summary>
        /// Coloca las cartas en arco solapado, ancladas abajo y asomando parcialmente fuera de
        /// pantalla. Se llama al re-renderizar la mano y en cada cambio de geometría de la franja
        /// (resolución). Itera por <see cref="_handEls"/> (orden estable): NO por los hijos de
        /// <c>_hand</c>, que <c>BringToFront</c> reordena en el hover.
        /// </summary>
        private void LayoutHand()
        {
            int n = _handEls.Count;
            if (n == 0) return;
            float w = _hand.resolvedStyle.width;
            if (w <= 1f) return;   // aún sin layout: el GeometryChangedEvent reintenta

            const float cardW = 160f;       // = .card width en Game.uss
            const float anglePer = 5f;      // grados de rotación por carta desde el centro
            const float restBottom = -118f; // empuje hacia abajo → asoman parcialmente
            const float arcK = 7f;          // cuánto caen las cartas hacia los bordes (arco)
            const float maxSpacing = 116f;  // separación ideal entre centros (< cardW → solapan)

            float spacing = Mathf.Min(maxSpacing, (w - cardW) / Mathf.Max(1, n - 1));
            float c = (n - 1) / 2f;

            for (int i = 0; i < n; i++)
            {
                float t = i - c;
                _handBaseRot[i] = t * anglePer;
                _handBaseBottom[i] = restBottom - t * t * arcK;

                VisualElement el = _handEls[i];
                el.style.left = w / 2f + t * spacing - cardW / 2f;
                el.style.bottom = _handBaseBottom[i];
                el.style.rotate = new Rotate(new Angle(_handBaseRot[i], AngleUnit.Degree));
                el.style.scale = new Scale(Vector3.one);
            }
        }

        /// <summary>Hover: la carta sube, se endereza, crece y se trae al frente (prominencia).</summary>
        private void OnCardHoverEnter(int i, VisualElement el)
        {
            if (_dragging) return;   // durante un drag no levantamos cartas ni mostramos el popover
            el.BringToFront();
            el.AddToClassList("card--hovered");
            el.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
            el.style.scale = new Scale(new Vector3(1.4f, 1.4f, 1f));
            el.style.bottom = -8f;

            // El popover de info se muestra recién al terminar la animación de hover (la carta ya
            // está enderezada y crecida), no de entrada. Se cancela si el puntero sale antes.
            _cardInfoPending?.Pause();
            _cardInfoPending = el.schedule.Execute(() => ShowCardInfo(i, el)).StartingIn(140);
        }

        /// <summary>Leave: la carta vuelve a su lugar/rotación en el abanico.</summary>
        private void OnCardHoverLeave(int i, VisualElement el)
        {
            _cardInfoPending?.Pause();   // cancela el popover diferido si el puntero salió antes de tiempo
            _cardInfoPending = null;
            HideInfoPopover();
            if (i >= _handBaseRot.Length) return;
            el.RemoveFromClassList("card--hovered");
            el.style.rotate = new Rotate(new Angle(_handBaseRot[i], AngleUnit.Degree));
            el.style.scale = new Scale(Vector3.one);
            el.style.bottom = _handBaseBottom[i];
        }

        /// <summary>
        /// Construye el visual de una carta (.card con nombre/costo/cuerpo).
        /// Lo comparten la mano y el ghost que acompaña el drag.
        /// </summary>
        private static VisualElement BuildCardVisual(CardData card, int inflationPercent = 0)
        {
            var el = new VisualElement();
            el.AddToClassList("card");

            // Header: nombre (izq) + badge de costo (arriba-derecha).
            var header = new VisualElement(); header.AddToClassList("card__header");
            var name = new Label(card.cardName); name.AddToClassList("card__name");
            var cost = new Label(CostText(card, inflationPercent)); cost.AddToClassList("card__cost");
            if (inflationPercent > 0) cost.AddToClassList("card__cost--inflated");
            header.Add(name); header.Add(cost);
            el.Add(header);

            // Art window (estilo Slay the Spire). Extensible: arte por-carta si el asset tiene su
            // propio sprite (CardData.sprite); si no, placeholder por facción+tipo.
            var art = new VisualElement(); art.AddToClassList("card__art");
            Texture2D artTex = CardArtTexture(card);
            if (card.sprite != null) art.style.backgroundImage = new StyleBackground(card.sprite);
            else if (artTex != null) art.style.backgroundImage = new StyleBackground(artTex);
            el.Add(art);

            var type = new Label(FaceTypeLabel(card)); type.AddToClassList("card__type");
            el.Add(type);

            // Cuerpo compacto: pills de stats para unidad/equipo; texto corto para acción. El
            // detalle completo (alcance, pasivas, deploy) vive en el popover de hover.
            el.Add(BuildCardFace(card));
            return el;
        }

        /// <summary>Cara compacta de la carta: pills (unidad/equipo) o texto corto (acción).</summary>
        private static VisualElement BuildCardFace(CardData card)
        {
            if (card is UnitCardData u)
            {
                var row = new VisualElement(); row.AddToClassList("card__pills");
                row.Add(CardPill($"❤ {u.maxHp}", "card__pill--hp"));   // ❤ HP
                int cnt = AttackTargetCount(u.attack);
                string val = cnt > 1 ? $"{u.attack.damagePerSlot}×{cnt}" : u.attack.damagePerSlot.ToString();
                row.Add(CardPill($"{(u.attack.IsHeal ? "✚" : "⚔")} {val}",
                                 u.attack.IsHeal ? "card__pill--heal" : "card__pill--atk"));
                return row;
            }
            if (card is EquipmentCardData eq)
            {
                var row = new VisualElement(); row.AddToClassList("card__pills");
                foreach (StatModifier m in eq.statModifiers)
                    row.Add(CardPill($"+{m.value} {(m.stat == StatType.MaxHp ? "❤" : "⚔")}",
                                     m.stat == StatType.MaxHp ? "card__pill--hp" : "card__pill--atk"));
                return row;
            }
            var body = new Label(EffectText(card)); body.AddToClassList("card__body");
            return body;   // acción: texto corto (recortado por el marco)
        }

        private static Label CardPill(string text, string variant)
        {
            var l = new Label(text);
            l.AddToClassList("card__pill");
            l.AddToClassList(variant);
            return l;
        }

        /// <summary>Etiqueta de tipo corta para la cara de la carta ("PODER" = Acción).</summary>
        private static string FaceTypeLabel(CardData c) => c switch
        {
            UnitCardData => "UNIDAD",
            EquipmentCardData => "EQUIPO",
            ActionCardData => "PODER",
            _ => ""
        };

        // Cache del arte placeholder por clave (también cachea el null para no recargar).
        private static readonly Dictionary<string, Texture2D> _cardArtCache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Arte placeholder de una carta por **facción + tipo** (spec §7.1): el mismo dibujo se repite
        /// para todas las cartas de esa combinación, vía <c>Resources/Images/card_art_{fac}_{tipo}</c>.
        /// Es el fallback; el arte por-carta definitivo va en <see cref="CardData.sprite"/> (prioridad
        /// en <see cref="BuildCardVisual"/>), así agregar arte propio luego es trivial y sin tocar esto.
        /// </summary>
        private static Texture2D CardArtTexture(CardData card)
        {
            string fac = card.faction == Faction.Manifestantes ? "manif" : "poli";
            string typ = card.CardType switch
            {
                CardType.Unidad => "unidad",
                CardType.Equipo => "equipo",
                _ => "poder",   // CardType.Accion = "Poder" en la grilla de arte
            };
            string key = "Images/card_art_" + fac + "_" + typ;
            if (!_cardArtCache.TryGetValue(key, out Texture2D tex))
            {
                tex = Resources.Load<Texture2D>(key);
                _cardArtCache[key] = tex;
            }
            return tex;
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
            if (_mode == Mode.Finished) return;

            // Ctrl+Click (botón izq) = descartar (sin botón DESCARTAR). Vale SIEMPRE que quede acción
            // de carta este turno: también con cartas que no podés pagar y aunque haya una carta armada
            // (en ese caso, primero cancela el targeting). No inicia drag.
            if (e.button == 0 && e.ctrlKey)
            {
                if (_engine.CardActionUsed) return;
                if (_mode != Mode.Acting) CancelTargeting();
                DoDiscard(index);
                return;
            }

            // Si hay una carta "armada" (esperando objetivo/confirmación), un click en la mano cancela.
            if (_mode != Mode.Acting) { CancelTargeting(); return; }

            if (_engine.CardActionUsed) return;

            _dragging = true;
            _dragIndex = index;
            _dragStartPos = e.position;
            card.CapturePointer(e.pointerId);
            AudioManager.Instance?.PlaySfx(AudioId.CardClick);
            HidePopover();
            HideInfoPopover();

            // El ghost es una copia visual de la carta (no solo el nombre) que sigue al puntero.
            _ghost = BuildCardVisual(_engine.ActivePlayer.hand[index], _engine.InflationPercent);
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

            // Sin botones JUGAR/DESCARTAR. Resolución del gesto:
            //   • soltada sobre un slot elegible        → DropOnSlot (jugar directo);
            //   • carta global soltada sobre CUALQUIER slot → jugar directo;
            //   • gesto sin desplazamiento (click)       → TryPlayCard (armar: pide confirmación/objetivo);
            //   • soltada fuera                          → se cancela (la carta vuelve a su lugar).
            Vector2 pos = e.position;
            float moved = (pos - _dragStartPos).magnitude;
            int slot = SlotUnderPointer(pos);
            if (slot >= 0) DropOnSlot(index, slot);
            else if (_dragGlobal && PointerOverAnySlot(pos)) DropGlobal(index);
            else if (moved <= ClickThreshold) TryPlayCard(index);
            // else: drop inválido → no se hace nada (la carta vuelve a su lugar al no re-renderizar).
        }

        // ── Drop sobre slot (carta arrastrada directo a su objetivo) ─────────────

        /// <summary>Calcula los slots que pueden recibir <paramref name="card"/> y los resalta.</summary>
        private void HighlightDropTargets(CardData card)
        {
            _dragEligibleSlots.Clear();
            _dragTargetSide = -1;
            _dragGlobal = false;

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

            if (_dragTargetSide < 0)
            {
                // Carta global (acción sin objetivo de slot): cualquier slot, de cualquier lado,
                // sirve para confirmar la jugada → resaltamos todos.
                if (card is ActionCardData)
                {
                    _dragGlobal = true;
                    foreach (VisualElement el in _p0SlotEls) el?.AddToClassList("slot--drop-ok");
                    foreach (VisualElement el in _p1SlotEls) el?.AddToClassList("slot--drop-ok");
                }
                return;
            }
            VisualElement[] els = _dragTargetSide == 0 ? _p0SlotEls : _p1SlotEls;
            foreach (int i in _dragEligibleSlots)
                els[i]?.AddToClassList("slot--drop-ok");
        }

        /// <summary>True si el puntero está sobre algún slot (de cualquier lado). Para cartas globales.</summary>
        private bool PointerOverAnySlot(Vector2 pos)
        {
            foreach (VisualElement el in _p0SlotEls)
                if (el != null && el.worldBound.Contains(pos)) return true;
            foreach (VisualElement el in _p1SlotEls)
                if (el != null && el.worldBound.Contains(pos)) return true;
            return false;
        }

        /// <summary>Juega una carta global directo (gesto de drag completo: no pide confirmación extra).</summary>
        private void DropGlobal(int index)
        {
            if (!_engine.CanAfford(index)) { _hint.text = "No te alcanzan los recursos"; return; }
            CardData card = _engine.ActivePlayer.hand[index];
            ResolveCardPlay(_engine.PlayCard(index), card);
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
            _firstTurnNotice.style.display = DisplayStyle.None;
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
            if (card is EquipmentCardData) return TargetType.Self;   // el equipo va sobre una unidad propia
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

        private static string CostText(CardData c, int inflationPercent = 0)
        {
            if (c.costs == null || c.costs.Count == 0) return "gratis";
            var parts = new List<string>();
            foreach (ResourceCost rc in c.costs)
                parts.Add($"{PlayerState.InflatedAmount(rc.amount, inflationPercent)}{ResSym(rc.resource)}");
            return string.Join("  ", parts);
        }

        private static string EffectText(CardData c)
        {
            var lines = new List<string>();

            if (c is UnitCardData u)
            {
                lines.Add($"HP {u.maxHp}");
                lines.Add(u.attack.IsHeal
                    ? $"Cura {u.attack.damagePerSlot} · {AttackShape(u.attack)}"
                    : $"Pega {u.attack.damagePerSlot} · {AttackShape(u.attack)}");
                foreach (PassiveEffect p in u.passiveEffects) lines.Add(PassiveText(p));
            }
            else if (c is EquipmentCardData eq)
            {
                foreach (StatModifier m in eq.statModifiers)
                    lines.Add($"+{m.value} {(m.stat == StatType.MaxHp ? "HP máx" : "daño")}");
                foreach (PassiveEffect p in eq.grantedPassives) lines.Add(PassiveText(p));
            }
            else if (c is ActionCardData a)
            {
                foreach (CardEffect e in a.effects) lines.Add(EffectPart(e));
            }

            return string.Join("\n", lines);
        }

        /// <summary>Forma del ataque/cura según su <see cref="TargetMode"/> (preview, sin formación concreta).</summary>
        private static string AttackShape(UnitAttack ua)
        {
            string side = ua.IsHeal ? "aliada" : "enemiga";
            switch (ua.mode)
            {
                case TargetMode.Frontmost:
                    return ua.count <= 1 ? $"a la {side} de adelante" : $"penetra a las {ua.count} de adelante";
                case TargetMode.Backmost:
                    return ua.count <= 1 ? $"a la {side} del fondo" : $"a las {ua.count} del fondo";
                case TargetMode.Any:
                    return ua.count <= 1 ? $"a una {side} a elección" : $"a {ua.count} {side}s a elección";
                case TargetMode.All:
                    return $"a todas las {side}s";
                case TargetMode.Adjacent:
                    return $"a las {side}s adyacentes";
                case TargetMode.Self:
                    return "a sí misma";
                default:
                    return "";
            }
        }

        /// <summary>Cantidad de golpes potenciales del ataque (para el "×N" de AoE en los iconos).</summary>
        private static int AttackTargetCount(UnitAttack ua)
        {
            switch (ua.mode)
            {
                case TargetMode.Frontmost:
                case TargetMode.Backmost:
                case TargetMode.Any:
                    return ua.count <= 0 ? 1 : ua.count;
                default:
                    return 1;   // All/Adjacent/Self: cantidad variable → sin ×N
            }
        }

        /// <summary>Nombre de zona si el patrón absoluto coincide con un preset (spec §6); si no, null.</summary>
        private static string AbsZoneName(int[] pattern)
        {
            var set = new HashSet<int>(pattern);
            if (set.SetEquals(new[] { 0, 1, 2 })) return "retaguardia";
            if (set.SetEquals(new[] { 3, 4, 5 })) return "vanguardia";
            return null;
        }

        private static string Slots1Based(int[] idxs)
        {
            var parts = new List<string>();
            foreach (int i in idxs) parts.Add((i + 1).ToString());
            parts.Sort();
            return string.Join(", ", parts);
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
            PlayerState opp = _engine.PlayerAt(0) == owner ? _engine.PlayerAt(1) : _engine.PlayerAt(0);
            PlayerState targetBoard = ua.IsHeal ? owner : opp;
            List<int> idxs = GameEngine.ResolveTargets(ua.mode, ua.count, targetBoard.unitSlots, slotIndex);
            idxs.Sort();

            string nums;
            if (idxs.Count == 0) nums = "ninguno ahora";
            else
            {
                var parts = new List<string>();
                foreach (int idx in idxs) parts.Add((idx + 1).ToString());  // 1-based para el usuario
                nums = "slots " + string.Join(", ", parts);
            }

            if (ua.IsHeal)
                return $"Cura {ua.damagePerSlot} HP · {AttackShape(ua)} ({nums})";

            int dmg = _engine.EffectiveAttackDamage(owner.unitSlots, slotIndex);
            return $"Pega {dmg} · {AttackShape(ua)} ({nums})";
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
            return $"{verb} {ua.damagePerSlot} · {AttackShape(ua)}";
        }

        private static string DeployZoneText(int[] allowed)
        {
            if (allowed == null || allowed.Length == 0) return "cualquier slot (1–6)";
            string zone = AbsZoneName(allowed);
            string slots = Slots1Based(allowed);
            return zone != null ? $"{zone} ({slots})" : $"slots {slots}";
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

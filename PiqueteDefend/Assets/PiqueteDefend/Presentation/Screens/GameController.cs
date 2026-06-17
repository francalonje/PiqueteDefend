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
    /// renderiza stats/slots/mano, maneja el flujo de turno hotseat (transición → producción → acción),
    /// la selección de target para sabotaje/slot lleno, y los overlays de transición y victoria.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameController : MonoBehaviour
    {
        private const string PanelSettingsResource = "UIPanelSettings";
        private const string CatalogResource = "CardCatalog";

        private enum Mode { Acting, TargetRemove, TargetSlotReplace, Finished }

        private GameEngine _engine;
        private Mode _mode;
        private int _selectedCard = -1;

        // UI refs
        private Label _turnBanner, _hint;
        private VisualElement _hand, _p0Slots, _p1Slots, _overlay;
        private Label _overlayTitle, _overlayMsg;
        private Button _overlayPrimary, _overlaySecondary, _playButton, _discardButton;
        private readonly Label[] _faction = new Label[2];
        private readonly Label[] _hp = new Label[2];
        private readonly Label[] _res = new Label[2];
        private readonly Label[] _status = new Label[2];

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (doc.panelSettings == null)
                doc.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);

            VisualElement root = doc.rootVisualElement;
            if (root == null) return;

            CacheRefs(root);
            ApplyBackground(root);

            var catalog = Resources.Load<CardCatalog>(CatalogResource);
            if (catalog == null)
            {
                Debug.LogError("[GameController] No se encontró CardCatalog en Resources.");
                return;
            }

            _engine = new GameEngine(new GameConfig(), new SystemRandomProvider(), catalog);
            _engine.StartGame(MatchConfig.Player0, MatchConfig.Player1);

            _playButton.clicked += OnPlay;
            _discardButton.clicked += OnDiscard;

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
            _playButton = root.Q<Button>("play-button");
            _discardButton = root.Q<Button>("discard-button");

            _overlay = root.Q<VisualElement>("overlay");
            _overlayTitle = root.Q<Label>("overlay-title");
            _overlayMsg = root.Q<Label>("overlay-msg");
            _overlayPrimary = root.Q<Button>("overlay-primary");
            _overlaySecondary = root.Q<Button>("overlay-secondary");

            _faction[0] = root.Q<Label>("p0-faction"); _faction[1] = root.Q<Label>("p1-faction");
            _hp[0] = root.Q<Label>("p0-hp"); _hp[1] = root.Q<Label>("p1-hp");
            _res[0] = root.Q<Label>("p0-res"); _res[1] = root.Q<Label>("p1-res");
            _status[0] = root.Q<Label>("p0-status"); _status[1] = root.Q<Label>("p1-status");
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
            _engine.BeginTurn();           // EFECTOS + PRODUCCIÓN + chequeo de victoria
            if (_engine.IsFinished) { ShowVictory(); return; }

            _mode = Mode.Acting;
            _selectedCard = -1;
            Render();
        }

        private void OnPlay()
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);

            if (_mode == Mode.TargetRemove || _mode == Mode.TargetSlotReplace)
            {
                CancelTargeting();
                return;
            }
            if (_mode != Mode.Acting || _selectedCard < 0) return;
            if (!_engine.CanAfford(_selectedCard)) return;

            if (_engine.RequiresRemoveTarget(_selectedCard)) { EnterTargeting(Mode.TargetRemove); return; }
            if (_engine.RequiresUnitSlotChoice(_selectedCard)) { EnterTargeting(Mode.TargetSlotReplace); return; }

            Resolve(_engine.PlayCard(_selectedCard));
        }

        private void OnDiscard()
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);

            if (_mode == Mode.TargetRemove || _mode == Mode.TargetSlotReplace) { CancelTargeting(); return; }
            if (_mode != Mode.Acting || _selectedCard < 0) return;
            Resolve(_engine.DiscardCard(_selectedCard));
        }

        private void Resolve(ActionResult result)
        {
            if (result != ActionResult.Success) return;   // precondición no cumplida: no pasa nada
            if (_engine.IsFinished) { ShowVictory(); return; }
            BeginActiveTurn();             // pasa directo al turno del siguiente jugador (sin click)
        }

        // ── Selección de target (sabotaje / slot lleno) ─────────────────────────

        private void EnterTargeting(Mode mode)
        {
            _mode = mode;
            _playButton.text = "Cancelar";
            _discardButton.SetEnabled(false);
            _hint.text = mode == Mode.TargetRemove
                ? "Elegí una unidad enemiga"
                : "Elegí tu slot a reemplazar";
            RenderSlots();   // re-render con slots clickeables
        }

        private void CancelTargeting()
        {
            _mode = Mode.Acting;
            _playButton.text = "Jugar";
            Render();
        }

        private void OnTargetChosen(int slotIndex)
        {
            int card = _selectedCard;
            ActionResult result = _mode == Mode.TargetRemove
                ? _engine.PlayCard(card, removeTargetSlot: slotIndex)
                : _engine.PlayCard(card, unitSlotToReplace: slotIndex);
            _playButton.text = "Jugar";
            Resolve(result);
        }

        // ── Render ──────────────────────────────────────────────────────────────

        private void Render()
        {
            _turnBanner.text = $"Turno: {_engine.ActivePlayer.faction}";
            _hint.text = string.Empty;
            RenderPanel(0);
            RenderPanel(1);
            RenderSlots();
            RenderHand();
            UpdateActionButtons();
        }

        private void RenderPanel(int index)
        {
            PlayerState p = _engine.PlayerAt(index);
            bool active = _engine.ActiveIndex == index;
            _faction[index].text = (active ? "▶ " : "") + p.faction;
            _hp[index].text = $"HP {p.hp}";
            _res[index].text = $"$ {p.dinero}   ⚡ {p.fuerza}   📣 {p.social}";
            _status[index].text = StatusText(p);
        }

        private void RenderSlots()
        {
            bool targetingEnemy = _mode == Mode.TargetRemove;
            bool targetingOwn = _mode == Mode.TargetSlotReplace;
            int activeIdx = _engine.ActiveIndex;

            RenderSlotColumn(_p0Slots, 0, targetingOwn && activeIdx == 0, targetingEnemy && activeIdx == 1);
            RenderSlotColumn(_p1Slots, 1, targetingOwn && activeIdx == 1, targetingEnemy && activeIdx == 0);
        }

        private void RenderSlotColumn(VisualElement column, int playerIndex, bool ownTarget, bool enemyTarget)
        {
            column.Clear();
            PlayerState p = _engine.PlayerAt(playerIndex);
            bool clickable = ownTarget || enemyTarget;

            for (int i = 0; i < p.unitSlots.Count; i++)
            {
                UnitSlot slot = p.unitSlots[i];
                var el = new VisualElement();
                el.AddToClassList("slot");
                var name = new Label(slot.unitData.cardName); name.AddToClassList("slot__name");
                var meta = new Label($"x{slot.count} · {UnitTag(slot.unitData)}"); meta.AddToClassList("slot__meta");
                el.Add(name); el.Add(meta);

                if (clickable)
                {
                    el.AddToClassList("slot--target");
                    int captured = i;
                    el.RegisterCallback<ClickEvent>(_ => OnTargetChosen(captured));
                }
                column.Add(el);
            }

            // Slots vacíos (para que se vean los 3)
            for (int i = p.unitSlots.Count; i < 3; i++)
            {
                var el = new VisualElement();
                el.AddToClassList("slot");
                el.AddToClassList("slot--empty");
                el.Add(new Label("—") { });
                column.Add(el);
            }
        }

        private void RenderHand()
        {
            _hand.Clear();
            PlayerState active = _engine.ActivePlayer;
            for (int i = 0; i < active.hand.Count; i++)
            {
                CardData card = active.hand[i];
                bool affordable = active.CanAfford(card);
                var el = new Button();
                el.AddToClassList("card");
                if (!affordable) el.AddToClassList("card--unaffordable");
                if (i == _selectedCard) el.AddToClassList("card--selected");

                var name = new Label(card.cardName); name.AddToClassList("card__name");
                var cost = new Label(CostText(card)); cost.AddToClassList("card__cost");
                var body = new Label(EffectText(card)); body.AddToClassList("card__body");
                el.Add(name); el.Add(cost); el.Add(body);

                int captured = i;
                el.clicked += () => SelectCard(captured);
                _hand.Add(el);
            }
        }

        private void SelectCard(int index)
        {
            if (_mode != Mode.Acting) return;
            AudioManager.Instance?.PlaySfx(AudioId.CardClick);
            _selectedCard = index;
            RenderHand();
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            bool hasSelection = _selectedCard >= 0;
            _discardButton.SetEnabled(hasSelection);
            _playButton.SetEnabled(hasSelection && _engine.CanAfford(_selectedCard));
        }

        // ── Overlays ──────────────────────────────────────────────────────────

        private void ShowVictory()
        {
            _mode = Mode.Finished;
            GameOutcome outcome = _engine.Outcome.Value;
            ShowOverlay(
                $"Ganó {outcome.Winner}",
                $"por {ConditionText(outcome.Condition)}",
                "Revancha", () => SceneManager.LoadScene("Game"),
                "Menú principal", () => SceneManager.LoadScene("Main"));
        }

        private void ShowOverlay(string title, string msg, string primary, Action onPrimary,
                                 string secondary, Action onSecondary)
        {
            _overlayTitle.text = title;
            _overlayMsg.text = msg;

            _overlayPrimary.text = primary;
            _overlayPrimary.clickable = new Clickable(() =>
            {
                AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
                onPrimary?.Invoke();
            });

            if (secondary != null)
            {
                _overlaySecondary.style.display = DisplayStyle.Flex;
                _overlaySecondary.text = secondary;
                _overlaySecondary.clickable = new Clickable(() =>
                {
                    AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
                    onSecondary?.Invoke();
                });
            }
            else
            {
                _overlaySecondary.style.display = DisplayStyle.None;
            }

            _overlay.style.display = DisplayStyle.Flex;
        }

        // ── Helpers de texto ────────────────────────────────────────────────────

        private static string StatusText(PlayerState p)
        {
            if (p.activeStatuses.Count == 0) return string.Empty;
            var parts = new List<string>();
            foreach (StatusEffect s in p.activeStatuses)
                parts.Add(s.statusType == StatusType.SkipProduction ? "⏭ sin producción" : "x2 producción");
            return string.Join("  ", parts);
        }

        private static string UnitTag(CardData c) => c.unitSubtype switch
        {
            UnitSubtype.Atacante => "atacante",
            UnitSubtype.Defensiva => "defensiva",
            UnitSubtype.Productora => $"produce {ResSym(c.productionResource)}",
            _ => ""
        };

        private static string CostText(CardData c)
        {
            var parts = new List<string>();
            if (c.costDinero > 0) parts.Add($"{c.costDinero}$");
            if (c.costFuerza > 0) parts.Add($"{c.costFuerza}⚡");
            if (c.costSocial > 0) parts.Add($"{c.costSocial}📣");
            return parts.Count == 0 ? "gratis" : string.Join("  ", parts);
        }

        private static string EffectText(CardData c)
        {
            if (c.cardType == CardType.Unidad)
            {
                return c.unitSubtype switch
                {
                    UnitSubtype.Atacante => "Unidad · +1 daño/turno",
                    UnitSubtype.Defensiva => "Unidad · −1 daño entrante/turno",
                    UnitSubtype.Productora => $"Unidad · +1 {ResSym(c.productionResource)}/turno",
                    _ => "Unidad"
                };
            }

            var parts = new List<string>();
            foreach (CardEffect e in c.effects) parts.Add(EffectPart(e));
            return string.Join("\n", parts);
        }

        private static string EffectPart(CardEffect e)
        {
            switch (e.effectType)
            {
                case CardEffectType.ModifyHP:
                    return e.value < 0 ? $"Daño {-e.value}" : $"Cura {e.value} HP";
                case CardEffectType.ModifyResource:
                    string who = e.target == TargetType.Opponent ? " al rival" : "";
                    string sign = e.value >= 0 ? "+" : "";
                    return $"{sign}{e.value} {ResSym(e.resourceTarget)}{who}";
                case CardEffectType.RemoveUnit:
                    return "−1 a una unidad enemiga";
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
            WinCondition.HegemoniaSocial => "Hegemonía Social",
            WinCondition.PoderEconomico => "Poder Económico",
            WinCondition.Timeout => "límite de turnos",
            _ => ""
        };
    }
}

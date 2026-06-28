using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Pantalla de tienda (spec §17.6): comprar cartas/reliquias con oro y pagar para sacar una carta.
    /// El stock vive en <see cref="RunManager.CurrentShop"/>; cada compra descuenta oro y re-renderiza.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShopController : MonoBehaviour
    {
        private const string PanelSettingsResource = "UIPanelSettings";

        private ScrollView _body;
        private Label _goldLabel;
        private bool _showRemoval;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (doc.panelSettings == null)
                doc.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);

            VisualElement root = doc.rootVisualElement;
            if (root == null) return;

            SceneBackground.Apply(root, "bg-menu");
            AudioManager.Instance?.PlayMusic(AudioId.MusicFactionSelect);

            if (!RunSession.IsActive || !RunSession.Manager.ShopInProgress)
            {
                SceneManager.LoadScene(RunSession.IsActive ? "Map" : "Main");
                return;
            }

            _body = root.Q<ScrollView>("shop-body");
            _goldLabel = root.Q<Label>("shop-gold");
            Button leave = root.Q<Button>("shop-leave");
            if (leave != null) leave.clicked += Leave;

            BuildContent();
        }

        private void BuildContent()
        {
            if (_body == null) return;
            RunManager rm = RunSession.Manager;
            ShopStock stock = rm.CurrentShop;
            if (stock == null) return;

            if (_goldLabel != null) _goldLabel.text = $"Oro: {rm.State.gold}";
            _body.Clear();

            // ── Cartas ─────────────────────────────────────────────────────────
            AddSectionTitle("Cartas");
            if (stock.cards.Count == 0)
            {
                AddEmpty("Agotadas.");
            }
            else
            {
                var grid = NewGrid();
                foreach (CardData card in stock.cards)
                {
                    CardData captured = card;
                    grid.Add(BuyItem(GameController.BuildCardSlot(card), stock.cardPrice,
                                     rm.State.gold >= stock.cardPrice, () => Buy(() => rm.BuyCard(captured))));
                }
            }

            // ── Reliquias ──────────────────────────────────────────────────────
            AddSectionTitle("Reliquias");
            if (stock.relics.Count == 0)
            {
                AddEmpty("Agotadas.");
            }
            else
            {
                var grid = NewGrid();
                foreach (RelicData relic in stock.relics)
                {
                    RelicData captured = relic;
                    grid.Add(BuyItem(BuildRelicCard(relic), stock.relicPrice,
                                     rm.State.gold >= stock.relicPrice, () => Buy(() => rm.BuyRelic(captured))));
                }
            }

            // ── Sacar una carta (servicio) ──────────────────────────────────────
            var removalBtn = new Button(() => { _showRemoval = !_showRemoval; BuildContent(); })
            {
                text = _showRemoval ? "Cancelar remoción" : $"Sacar una carta ({stock.removalPrice})"
            };
            removalBtn.AddToClassList("btn");
            removalBtn.style.marginTop = 14;
            _body.Add(removalBtn);

            if (_showRemoval)
            {
                AddSectionTitle("Elegí qué carta sacar");
                var grid = NewGrid();
                foreach (CardData card in rm.State.deck)
                {
                    CardData captured = card;
                    grid.Add(BuyItem(GameController.BuildCardSlot(card), stock.removalPrice,
                                     rm.State.gold >= stock.removalPrice,
                                     () => Buy(() => rm.BuyRemoval(captured)), "Quitar"));
                }
            }
        }

        // ── Helpers de armado ──────────────────────────────────────────────────

        private VisualElement NewGrid()
        {
            var grid = new VisualElement();
            grid.AddToClassList("run-grid");
            _body.Add(grid);
            return grid;
        }

        private void AddSectionTitle(string text)
        {
            var t = new Label(text);
            t.AddToClassList("run-section-title");
            _body.Add(t);
        }

        private void AddEmpty(string text)
        {
            var e = new Label(text);
            e.AddToClassList("run-empty");
            _body.Add(e);
        }

        /// <summary>Envuelve un visual (carta/reliquia) con un botón de acción con precio.</summary>
        private static VisualElement BuyItem(VisualElement visual, int price, bool affordable,
                                             System.Action onClick, string verb = "Comprar")
        {
            var item = new VisualElement();
            item.AddToClassList("shop-item");
            if (!affordable) item.AddToClassList("shop-item--sold");
            item.Add(visual);

            var btn = new Button(onClick) { text = $"{verb} ({price})" };
            btn.AddToClassList("btn");
            btn.AddToClassList("btn--primary");
            btn.AddToClassList("shop-item__action");
            btn.SetEnabled(affordable);
            item.Add(btn);
            return item;
        }

        /// <summary>Tarjeta placeholder de reliquia (mismo gálibo que una carta). Sprite a futuro.</summary>
        private static VisualElement BuildRelicCard(RelicData relic)
        {
            var card = new VisualElement();
            card.AddToClassList("relic-card");

            var badge = new Label("RELIQUIA");
            badge.AddToClassList("relic-card__badge");
            var name = new Label(relic.relicName);
            name.AddToClassList("relic-card__name");
            var desc = new Label(relic.description);
            desc.AddToClassList("relic-card__desc");

            card.Add(badge);
            card.Add(name);
            card.Add(desc);
            return card;
        }

        private void Buy(System.Action purchase)
        {
            AudioManager.Instance?.PlaySfx(AudioId.CardPlay);
            purchase();
            BuildContent();   // re-render: oro actualizado, item vendido
        }

        private void Leave()
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            RunSession.Manager.LeaveShop();
            SceneManager.LoadScene("Map");
        }
    }
}

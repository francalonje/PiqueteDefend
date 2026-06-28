using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Pantalla de recompensa de la run (spec §17.2): muestra la oferta 1-de-3 de
    /// <see cref="RunManager.PendingReward"/>. Elegir suma la carta al mazo de la run; saltear no suma.
    /// En ambos casos vuelve al mapa.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RewardController : MonoBehaviour
    {
        private const string PanelSettingsResource = "UIPanelSettings";

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (doc.panelSettings == null)
                doc.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);

            VisualElement root = doc.rootVisualElement;
            if (root == null) return;

            SceneBackground.Apply(root, "bg-menu");
            AudioManager.Instance?.PlayMusic(AudioId.MusicFactionSelect);

            // Sin run o sin oferta pendiente: volvé al mapa (defensivo).
            if (!RunSession.IsActive || !RunSession.Manager.PendingReward.HasReward)
            {
                SceneManager.LoadScene(RunSession.IsActive ? "Map" : "Main");
                return;
            }

            var cards = root.Q<VisualElement>("reward-cards");
            Button skip = root.Q<Button>("reward-skip");
            if (skip != null) skip.clicked += SkipReward;

            if (cards == null) return;
            cards.Clear();
            foreach (CardData card in RunSession.Manager.PendingReward.cards)
                cards.Add(BuildRewardCard(card));
        }

        /// <summary>Cada recompensa se ve como una carta de la mano: reusa el visual de
        /// <see cref="GameController.BuildCardVisual"/> (imagen + nombre + costo + pills). Se envuelve
        /// en un contenedor clickeable que la elige.</summary>
        private VisualElement BuildRewardCard(CardData card)
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("reward-pick");

            VisualElement cardView = GameController.BuildCardSlot(card);
            wrap.Add(cardView);

            wrap.RegisterCallback<ClickEvent>(_ => Choose(card));
            return wrap;
        }

        private void Choose(CardData card)
        {
            AudioManager.Instance?.PlaySfx(AudioId.CardPlay);
            RunSession.Manager.ChooseReward(card);
            SceneManager.LoadScene("Map");
        }

        private void SkipReward()
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            RunSession.Manager.SkipReward();
            SceneManager.LoadScene("Map");
        }
    }
}

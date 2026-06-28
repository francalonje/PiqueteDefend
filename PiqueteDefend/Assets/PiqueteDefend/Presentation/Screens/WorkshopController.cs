using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Pantalla de taller de mazo (spec §17.6): muestra el mazo de la run y deja quitar UNA carta
    /// (gratis) o seguir sin tocar. Reusa el visual de carta de <see cref="GameController.BuildCardVisual"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class WorkshopController : MonoBehaviour
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

            if (!RunSession.IsActive || !RunSession.Manager.WorkshopInProgress)
            {
                SceneManager.LoadScene(RunSession.IsActive ? "Map" : "Main");
                return;
            }

            RunManager rm = RunSession.Manager;
            var scroll = root.Q<ScrollView>("wk-items");
            var subtitle = root.Q<Label>("wk-subtitle");
            Button leave = root.Q<Button>("wk-leave");
            if (leave != null) leave.clicked += Leave;

            bool canRemove = rm.CanRemoveCard;
            if (!canRemove && subtitle != null)
                subtitle.text = "El mazo está en el mínimo: no podés quitar más cartas. Seguí.";

            if (scroll == null) return;
            var grid = new VisualElement();
            grid.AddToClassList("run-grid");
            scroll.Add(grid);

            foreach (CardData card in rm.WorkshopCards)
            {
                var item = new VisualElement();
                item.AddToClassList("shop-item");
                item.Add(GameController.BuildCardSlot(card));

                CardData captured = card;
                var btn = new Button(() => Remove(captured)) { text = "Quitar" };
                btn.AddToClassList("btn");
                btn.AddToClassList("btn--primary");
                btn.AddToClassList("shop-item__action");
                btn.SetEnabled(canRemove);
                item.Add(btn);

                grid.Add(item);
            }
        }

        private void Remove(CardData card)
        {
            AudioManager.Instance?.PlaySfx(AudioId.CardPlay);
            RunSession.Manager.RemoveCardAndLeave(card);
            SceneManager.LoadScene("Map");
        }

        private void Leave()
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            RunSession.Manager.LeaveWorkshop();
            SceneManager.LoadScene("Map");
        }
    }
}

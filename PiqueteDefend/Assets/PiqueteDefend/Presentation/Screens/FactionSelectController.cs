using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Selección de facción (spec §11.2). Por ahora los lados son fijos (Manifestantes a la
    /// izquierda, Policías a la derecha): la elección solo decide qué facción juega primero.
    /// Guarda <see cref="MatchConfig.StartingFaction"/> y pasa a la pantalla de juego.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class FactionSelectController : MonoBehaviour
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

            Label prompt = root.Q<Label>("prompt");
            if (prompt != null) prompt.text = "Elegí la facción que juega primero";

            Button manif = root.Q<Button>("manifestantes-button");
            Button pol = root.Q<Button>("policias-button");
            if (manif != null) manif.clicked += () => Pick(Faction.Manifestantes);
            if (pol != null) pol.clicked += () => Pick(Faction.Policias);
        }

        private void Pick(Faction faction)
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            MatchConfig.StartingFaction = faction;
            SceneManager.LoadScene("Game");
        }
    }
}

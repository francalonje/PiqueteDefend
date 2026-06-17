using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Selección de facción (spec §11.2). Hotseat: primero elige el Jugador 1, luego el Jugador 2
    /// (pueden repetir facción). Guarda en <see cref="MatchConfig"/> y pasa a la pantalla de juego.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class FactionSelectController : MonoBehaviour
    {
        private const string PanelSettingsResource = "UIPanelSettings";

        private int _step;
        private Label _prompt;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (doc.panelSettings == null)
                doc.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);

            VisualElement root = doc.rootVisualElement;
            if (root == null) return;

            _step = 0;
            _prompt = root.Q<Label>("prompt");

            Button manif = root.Q<Button>("manifestantes-button");
            Button pol = root.Q<Button>("policias-button");
            if (manif != null) manif.clicked += () => Pick(Faction.Manifestantes);
            if (pol != null) pol.clicked += () => Pick(Faction.Policias);
        }

        private void Pick(Faction faction)
        {
            if (_step == 0)
            {
                MatchConfig.Player0 = faction;
                _step = 1;
                if (_prompt != null) _prompt.text = "Jugador 2 — elegí tu facción";
                return;
            }

            MatchConfig.Player1 = faction;
            // TODO Fase 4: cargar la escena de juego cuando exista.
            Debug.Log($"[FactionSelect] Jugador 1 = {MatchConfig.Player0}, Jugador 2 = {MatchConfig.Player1}.");
        }
    }
}

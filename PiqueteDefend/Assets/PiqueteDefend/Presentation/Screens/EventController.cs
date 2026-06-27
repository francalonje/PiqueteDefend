using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Pantalla de evento (spec §17.6): muestra el título/cuerpo del evento y sus opciones; elegir una
    /// aplica sus resultados (oro/reliquia/carta) y vuelve al mapa. Sin salida sin elegir.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class EventController : MonoBehaviour
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

            if (!RunSession.IsActive || !RunSession.Manager.EventInProgress)
            {
                SceneManager.LoadScene(RunSession.IsActive ? "Map" : "Main");
                return;
            }

            RunManager rm = RunSession.Manager;
            EventDefinition ev = rm.CurrentEvent;

            var title = root.Q<Label>("ev-title");
            var body = root.Q<Label>("ev-body");
            var gold = root.Q<Label>("ev-gold");
            var choices = root.Q<VisualElement>("ev-choices");

            if (title != null) title.text = ev.title;
            if (body != null) body.text = ev.body;
            if (gold != null) gold.text = $"Oro: {rm.State.gold}";

            if (choices == null) return;
            choices.Clear();
            for (int i = 0; i < ev.choices.Count; i++)
            {
                int captured = i;
                var btn = new Button(() => Resolve(captured)) { text = ev.choices[i].text };
                btn.AddToClassList("btn");
                btn.AddToClassList("event-choice");
                if (i == 0) btn.AddToClassList("btn--primary");
                choices.Add(btn);
            }
        }

        private void Resolve(int choiceIndex)
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            RunSession.Manager.ResolveEvent(choiceIndex);
            SceneManager.LoadScene("Map");
        }
    }
}

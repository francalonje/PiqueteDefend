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
        private const string CatalogResource = "CardCatalog";

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
            if (prompt != null)
                prompt.text = MatchConfig.Mode == GameMode.Run
                    ? "Elegí tu facción"                       // run: el rival lo juega la IA
                    : "Elegí la facción que juega primero";    // hotseat: sólo decide la iniciativa

            Button manif = root.Q<Button>("manifestantes-button");
            Button pol = root.Q<Button>("policias-button");
            if (manif != null) manif.clicked += () => Pick(Faction.Manifestantes);
            if (pol != null) pol.clicked += () => Pick(Faction.Policias);
        }

        private void Pick(Faction faction)
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);

            if (MatchConfig.Mode == GameMode.Run)
            {
                StartRun(faction);
                return;
            }

            // Hotseat: sólo decide quién arranca; los lados son fijos.
            MatchConfig.StartingFaction = faction;
            SceneManager.LoadScene("Game");
        }

        /// <summary>Arranca una run con la facción elegida y abre el mapa (spec §17).</summary>
        private void StartRun(Faction faction)
        {
            var catalog = Resources.Load<CardCatalog>(CatalogResource);
            if (catalog == null)
            {
                Debug.LogError("[FactionSelect] No se encontró CardCatalog en Resources.");
                return;
            }

            // Acto 1 = Línea A del subte (§17.1) + pool de arquetipos de enemigo (§17.6) + reliquias (§17.4).
            var encounters = EncounterLibrary.BuildActo1Pool(catalog);
            var relics = RelicLibrary.BuildPool(catalog, faction);
            var run = new RunManager(catalog, new GameConfig(), new SystemRandomProvider(), faction,
                                     RunMapLibrary.BuildActo1(), null, encounters, relics);
            RunSession.Start(run);
            SceneManager.LoadScene("Map");
        }
    }
}

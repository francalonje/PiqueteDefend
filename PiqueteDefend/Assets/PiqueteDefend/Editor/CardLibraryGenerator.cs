using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PiqueteDefend.Core;

namespace PiqueteDefend.EditorTools
{
    /// <summary>
    /// Genera los assets de cartas (ScriptableObjects) y el CardCatalog a partir de
    /// <see cref="CardLibrary"/>, la fuente de verdad en código. Idempotente: borra y
    /// regenera la carpeta Data/Cards completa, así un cambio de balance se re-aplica con un click.
    ///
    /// Menú: PiqueteDefend → Generate Card Library.
    /// Batch: -executeMethod PiqueteDefend.EditorTools.CardLibraryGenerator.GenerateAll
    /// </summary>
    public static class CardLibraryGenerator
    {
        private const string ParentFolder = "Assets/PiqueteDefend";
        private const string DataFolder = ParentFolder + "/Data";
        private const string CardsFolder = DataFolder + "/Cards";
        private const string ResourcesFolder = ParentFolder + "/Presentation/Resources";

        /// <summary>Nombre con el que el runtime carga el catálogo vía Resources.Load.</summary>
        public const string CatalogResourceName = "CardCatalog";

        [MenuItem("PiqueteDefend/Generate Card Library")]
        public static void GenerateAll()
        {
            if (AssetDatabase.IsValidFolder(DataFolder))
                AssetDatabase.DeleteAsset(DataFolder);

            AssetDatabase.CreateFolder(ParentFolder, "Data");
            AssetDatabase.CreateFolder(DataFolder, "Cards");
            AssetDatabase.CreateFolder(CardsFolder, "Manifestantes");
            AssetDatabase.CreateFolder(CardsFolder, "Policias");

            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
                AssetDatabase.CreateFolder(ParentFolder + "/Presentation", "Resources");

            var catalog = ScriptableObject.CreateInstance<CardCatalog>();
            catalog.manifestantes = Persist(CardLibrary.BuildManifestantes(), "Manifestantes");
            catalog.policias = Persist(CardLibrary.BuildPolicias(), "Policias");
            // El catálogo vive en Resources/ para cargarlo en runtime (Resources.Load).
            AssetDatabase.CreateAsset(catalog, $"{ResourcesFolder}/{CatalogResourceName}.asset");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int total = catalog.manifestantes.Count + catalog.policias.Count;
            Debug.Log($"[CardLibraryGenerator] Generadas {total} cartas (Data/) + CardCatalog (Resources/).");
        }

        private static List<CardData> Persist(List<CardData> cards, string factionFolder)
        {
            foreach (CardData card in cards)
                AssetDatabase.CreateAsset(card, $"{CardsFolder}/{factionFolder}/{card.id}.asset");
            return cards;
        }
    }
}

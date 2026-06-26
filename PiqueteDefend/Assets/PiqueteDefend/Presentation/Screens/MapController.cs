using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Pantalla de mapa de la run (spec §17.1): puntos a elección posicionados por sus coords x/y,
    /// con las conexiones dibujadas como líneas. El jugador clickea un punto disponible (sucesor del
    /// actual) → arma el combate (<see cref="RunManager.BeginCombat"/>) y abre la escena de juego.
    /// 2D UI Toolkit (MVP); el diorama 3D queda como mejora futura.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapController : MonoBehaviour
    {
        private const string PanelSettingsResource = "UIPanelSettings";

        private VisualElement _area;
        private VisualElement _edges;
        private readonly Dictionary<int, VisualElement> _nodeEls = new Dictionary<int, VisualElement>();

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (doc.panelSettings == null)
                doc.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);

            VisualElement root = doc.rootVisualElement;
            if (root == null) return;

            SceneBackground.Apply(root, "bg-menu");
            AudioManager.Instance?.PlayMusic(AudioId.MusicFactionSelect);

            // Sin run en curso (escena abierta suelta, o run terminada): volvé al menú.
            if (!RunSession.IsActive || RunSession.Manager.State.status != RunStatus.InProgress)
            {
                SceneManager.LoadScene("Main");
                return;
            }

            _area = root.Q<VisualElement>("map-area");
            Button abandon = root.Q<Button>("map-abandon");
            if (abandon != null) abandon.clicked += Abandon;

            BuildMap();
        }

        private void BuildMap()
        {
            if (_area == null) return;
            _area.Clear();
            _nodeEls.Clear();

            // Capa de líneas detrás de los puntos.
            _edges = new VisualElement { name = "map-edges", pickingMode = PickingMode.Ignore };
            _edges.style.position = Position.Absolute;
            _edges.style.left = 0; _edges.style.top = 0; _edges.style.right = 0; _edges.style.bottom = 0;
            _area.Add(_edges);

            RunManager run = RunSession.Manager;
            RunState st = run.State;
            RunMap map = st.map;

            var available = new HashSet<int>();
            foreach (MapNode n in run.AvailableNodes()) available.Add(n.id);

            foreach (MapNode node in map.Nodes)
            {
                var btn = new Button { text = node.title };
                btn.AddToClassList("map-node");
                btn.style.left = Length.Percent(Mathf.Clamp01(node.x) * 100f);
                btn.style.top = Length.Percent(Mathf.Clamp01(node.y) * 100f);

                bool isCurrent = node.id == st.currentNodeId;
                bool isCleared = st.IsCleared(node.id) && !isCurrent;
                bool isAvailable = available.Contains(node.id);

                if (node.type == MapNodeType.Boss) btn.AddToClassList("map-node--boss");
                if (isCurrent) btn.AddToClassList("map-node--current");
                else if (isCleared) btn.AddToClassList("map-node--cleared");
                else if (isAvailable) btn.AddToClassList("map-node--available");
                else btn.AddToClassList("map-node--locked");

                if (isAvailable)
                {
                    int id = node.id;
                    btn.clicked += () => ChooseNode(id);
                }
                else
                {
                    btn.SetEnabled(false);   // no clickeable, pero no por estilo :disabled (mantiene color de estado)
                    btn.pickingMode = PickingMode.Ignore;
                }

                _area.Add(btn);
                _nodeEls[node.id] = btn;
            }

            // Las líneas necesitan el layout ya resuelto: las (re)dibujamos cuando el área tiene geometría.
            _area.RegisterCallback<GeometryChangedEvent>(_ => DrawEdges());
        }

        /// <summary>Dibuja una línea por cada conexión, anclada a los centros ya posicionados.</summary>
        private void DrawEdges()
        {
            if (_edges == null) return;
            _edges.Clear();

            RunState st = RunSession.Manager.State;
            foreach (MapNode node in st.map.Nodes)
            {
                if (!_nodeEls.TryGetValue(node.id, out VisualElement fromEl)) continue;
                Vector2 from = Center(fromEl);
                if (float.IsNaN(from.x)) continue;

                foreach (int toId in node.connections)
                {
                    if (!_nodeEls.TryGetValue(toId, out VisualElement toEl)) continue;
                    Vector2 to = Center(toEl);
                    if (float.IsNaN(to.x)) continue;

                    Vector2 d = to - from;
                    float len = d.magnitude;
                    if (len < 1f) continue;
                    float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

                    var line = new VisualElement();
                    line.AddToClassList("map-edge");
                    if (node.id == st.currentNodeId) line.AddToClassList("map-edge--open");
                    line.style.left = from.x;
                    line.style.top = from.y;
                    line.style.width = len;
                    line.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
                    _edges.Add(line);
                }
            }
        }

        private static Vector2 Center(VisualElement el)
        {
            Rect r = el.layout;
            if (float.IsNaN(r.width) || float.IsNaN(r.x)) return new Vector2(float.NaN, float.NaN);
            return new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
        }

        private void ChooseNode(int nodeId)
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            GameEngine engine = RunSession.Manager.BeginCombat(nodeId);
            RunSession.SetPendingCombat(engine);
            SceneManager.LoadScene("Game");
        }

        private void Abandon()
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            RunSession.Clear();
            SceneManager.LoadScene("Main");
        }
    }
}

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
        private Label _goldLabel;
        private VisualElement _relicsBar;
        private Label _toast;
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

            EnsureHud(root);
            BuildMap();
        }

        /// <summary>HUD de run (oro + reliquias + toast). Construido en runtime (no depende del UXML).
        /// Placeholder: las reliquias se muestran como chips con nombre; el sprite va más adelante.</summary>
        private void EnsureHud(VisualElement root)
        {
            if (_goldLabel != null) return;

            _goldLabel = new Label { name = "map-gold" };
            _goldLabel.style.position = Position.Absolute;
            _goldLabel.style.top = 12;
            _goldLabel.style.left = 12;
            _goldLabel.style.fontSize = 20;
            _goldLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _goldLabel.style.color = new Color(1f, 0.85f, 0.25f);   // dorado
            root.Add(_goldLabel);

            // Barra de reliquias (fila de chips placeholder, arriba a la derecha).
            _relicsBar = new VisualElement { name = "map-relics" };
            _relicsBar.style.position = Position.Absolute;
            _relicsBar.style.top = 12;
            _relicsBar.style.right = 12;
            _relicsBar.style.flexDirection = FlexDirection.Row;
            root.Add(_relicsBar);

            // Toast de feedback (centro arriba), p. ej. al sacar una reliquia del tesoro.
            _toast = new Label { name = "map-toast" };
            _toast.style.position = Position.Absolute;
            _toast.style.top = 44;
            _toast.style.left = 0;
            _toast.style.right = 0;
            _toast.style.unityTextAlign = TextAnchor.MiddleCenter;
            _toast.style.fontSize = 18;
            _toast.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toast.style.color = new Color(1f, 0.95f, 0.7f);
            _toast.style.display = DisplayStyle.None;
            root.Add(_toast);
        }

        private void UpdateHud()
        {
            if (!RunSession.IsActive) return;
            RunState st = RunSession.Manager.State;

            if (_goldLabel != null) _goldLabel.text = $"Oro: {st.gold}";

            if (_relicsBar != null)
            {
                _relicsBar.Clear();
                foreach (RelicData relic in st.relics)
                {
                    if (relic == null) continue;
                    var chip = new Label(Short(relic.relicName)) { tooltip = $"{relic.relicName}\n{relic.description}" };
                    chip.AddToClassList("relic-chip");
                    chip.style.marginLeft = 4;
                    chip.style.paddingLeft = 6; chip.style.paddingRight = 6;
                    chip.style.paddingTop = 2; chip.style.paddingBottom = 2;
                    chip.style.backgroundColor = new Color(0.15f, 0.12f, 0.05f, 0.9f);
                    chip.style.borderTopLeftRadius = 4; chip.style.borderTopRightRadius = 4;
                    chip.style.borderBottomLeftRadius = 4; chip.style.borderBottomRightRadius = 4;
                    chip.style.color = new Color(1f, 0.85f, 0.25f);
                    chip.style.fontSize = 13;
                    _relicsBar.Add(chip);
                }
            }
        }

        /// <summary>Iniciales del nombre de la reliquia (placeholder hasta tener sprites).</summary>
        private static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            return name.Length <= 12 ? name : name.Substring(0, 12) + "…";
        }

        private void ShowToast(string text)
        {
            if (_toast == null) return;
            _toast.text = text;
            _toast.style.display = DisplayStyle.Flex;
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

                // Clase visual por tipo (el USS puede no tener todas; AddToClassList es inofensivo).
                btn.AddToClassList(NodeTypeClass(node.type));
                if (isCurrent) btn.AddToClassList("map-node--current");
                else if (isCleared) btn.AddToClassList("map-node--cleared");
                else if (isAvailable) btn.AddToClassList("map-node--available");
                else btn.AddToClassList("map-node--locked");

                if (isAvailable && TryGetNodeAction(node.type, node.id, out System.Action onClick))
                {
                    btn.clicked += onClick;
                }
                else
                {
                    btn.SetEnabled(false);   // no clickeable, pero no por estilo :disabled (mantiene color de estado)
                    btn.pickingMode = PickingMode.Ignore;
                }

                _area.Add(btn);
                _nodeEls[node.id] = btn;
            }

            UpdateHud();

            // Las líneas necesitan el layout ya resuelto: las (re)dibujamos cuando el área tiene geometría.
            _area.RegisterCallback<GeometryChangedEvent>(_ => DrawEdges());
        }

        /// <summary>Clase USS por tipo de nodo (spec §17.6).</summary>
        private static string NodeTypeClass(MapNodeType type) => type switch
        {
            MapNodeType.Boss => "map-node--boss",
            MapNodeType.Elite => "map-node--elite",
            MapNodeType.Treasure => "map-node--treasure",
            MapNodeType.Shop => "map-node--shop",
            MapNodeType.Event => "map-node--event",
            MapNodeType.Workshop => "map-node--workshop",
            MapNodeType.Mystery => "map-node--mystery",
            _ => "map-node--combat",
        };

        /// <summary>Acción al clickear un nodo disponible, según su tipo. Devuelve false para tipos aún
        /// no implementados (quedan no-clickeables; el acto 1 no los usa, spec §17.6).</summary>
        private bool TryGetNodeAction(MapNodeType type, int nodeId, out System.Action onClick)
        {
            switch (type)
            {
                case MapNodeType.Combat:
                case MapNodeType.Elite:
                case MapNodeType.Boss:
                    onClick = () => ChooseNode(nodeId);
                    return true;
                case MapNodeType.Treasure:
                    onClick = () => ChooseTreasure(nodeId);
                    return true;
                case MapNodeType.Workshop:
                    onClick = () => EnterScene(() => RunSession.Manager.EnterWorkshop(nodeId), "Workshop");
                    return true;
                case MapNodeType.Shop:
                    onClick = () => EnterScene(() => RunSession.Manager.EnterShop(nodeId), "Shop");
                    return true;
                case MapNodeType.Event:
                    onClick = () => EnterScene(() => RunSession.Manager.EnterEvent(nodeId), "Event");
                    return true;
                default:
                    onClick = null;
                    return false;
            }
        }

        /// <summary>Abre la interacción del nodo en el Core y carga su escena (taller/tienda/evento).</summary>
        private void EnterScene(System.Action open, string scene)
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            open();
            SceneManager.LoadScene(scene);
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

        /// <summary>Tesoro (spec §17.6): da una reliquia (o oro si no quedan) y avanza, sin salir del mapa.</summary>
        private void ChooseTreasure(int nodeId)
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            TreasureReward reward = RunSession.Manager.EnterTreasure(nodeId);
            BuildMap();   // refresca oro, reliquias, nodo actual y sucesores disponibles
            ShowToast(reward.IsRelic ? $"¡Reliquia: {reward.relic.relicName}!" : $"+{reward.gold} oro");
        }

        private void Abandon()
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            RunSession.Clear();
            SceneManager.LoadScene("Main");
        }
    }
}

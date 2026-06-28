using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Pantalla de mapa de la run (spec §17.1): la <b>tira de la línea de subte del acto</b>. Las
    /// estaciones se posicionan por su <c>x</c> a lo largo de una barra del color de la línea; el
    /// jugador avanza 1 o 2 paradas (los sucesores del actual quedan resaltados) y las salteadas
    /// quedan atrás (una sola pasada). Cada parada abre su encuentro (combate/tienda/evento/…).
    /// Look recreado con UI Toolkit (sin arte de terceros); sprite-ready vía <see cref="IconLoader"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapController : MonoBehaviour
    {
        private const string PanelSettingsResource = "UIPanelSettings";

        // Geometría de la tira (porcentajes del área; coherente con RunMapLibrary.BuildLineaA).
        private const float DotOffset = 18f;   // px: separación del dot a las etiquetas/combos arriba-abajo.

        private VisualElement _area;
        private VisualElement _goldRow;
        private Label _goldLabel;
        private VisualElement _relicsBar;
        private Label _toast;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (doc.panelSettings == null)
                doc.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);

            VisualElement root = doc.rootVisualElement;
            if (root == null) return;

            // Fondo: base de menú + override de subte si existe el recurso (hook listo, aparece solo).
            SceneBackground.Apply(root, "bg-menu");
            SceneBackground.Apply(root, "bg-subte");
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

        // ── HUD de run (oro + reliquias + toast) ────────────────────────────────────

        /// <summary>HUD de run construido en runtime (no depende del UXML): oro (ícono + número),
        /// reliquias (chips sprite-ready) y toast de feedback.</summary>
        private void EnsureHud(VisualElement root)
        {
            if (_goldRow != null) return;

            _goldRow = new VisualElement { name = "map-gold" };
            _goldRow.AddToClassList("hud-gold");
            _goldRow.style.position = Position.Absolute;
            _goldRow.style.top = 12;
            _goldRow.style.left = 12;
            VisualElement goldIcon = IconLoader.BuildIcon("gold", "$", "hud-gold__icon");
            if (goldIcon != null) _goldRow.Add(goldIcon);
            _goldLabel = new Label();
            _goldLabel.AddToClassList("hud-gold__value");
            _goldRow.Add(_goldLabel);
            root.Add(_goldRow);

            _relicsBar = new VisualElement { name = "map-relics" };
            _relicsBar.AddToClassList("hud-relics");
            _relicsBar.style.position = Position.Absolute;
            _relicsBar.style.top = 12;
            _relicsBar.style.right = 12;
            _relicsBar.style.flexDirection = FlexDirection.Row;
            root.Add(_relicsBar);

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

            if (_goldLabel != null) _goldLabel.text = st.gold.ToString();

            if (_relicsBar != null)
            {
                _relicsBar.Clear();
                foreach (RelicData relic in st.relics)
                {
                    if (relic == null) continue;
                    _relicsBar.Add(GameController.BuildRelicChip(relic));
                }
            }
        }

        private void ShowToast(string text)
        {
            if (_toast == null) return;
            _toast.text = text;
            _toast.style.display = DisplayStyle.Flex;
        }

        // ── Tira de la línea ────────────────────────────────────────────────────────

        private void BuildMap()
        {
            if (_area == null) return;
            _area.Clear();

            RunManager run = RunSession.Manager;
            RunState st = run.State;
            RunMap map = st.map;
            Color lineColor = LineColor(map);

            var available = new HashSet<int>();
            foreach (MapNode n in run.AvailableNodes()) available.Add(n.id);

            DrawLineBar(map, lineColor);

            foreach (MapNode node in map.Nodes)
            {
                bool isCurrent = node.id == st.currentNodeId;
                bool isCleared = st.IsCleared(node.id) && !isCurrent;
                bool isAvailable = available.Contains(node.id);

                BuildStation(node, lineColor, isCurrent, isCleared, isAvailable);
            }

            UpdateHud();
        }

        /// <summary>Barra continua del color de la línea, detrás de las estaciones (de la primera x a la
        /// última, a media altura). Es "la línea" del subte.</summary>
        private void DrawLineBar(RunMap map, Color lineColor)
        {
            float minX = 1f, maxX = 0f;
            foreach (MapNode n in map.Nodes)
            {
                if (n.x < minX) minX = n.x;
                if (n.x > maxX) maxX = n.x;
            }
            if (maxX < minX) return;

            var bar = new VisualElement { name = "map-line", pickingMode = PickingMode.Ignore };
            bar.AddToClassList("map-line");
            bar.style.position = Position.Absolute;
            bar.style.left = Length.Percent(minX * 100f);
            bar.style.width = Length.Percent((maxX - minX) * 100f);
            bar.style.top = Length.Percent(50f);
            bar.style.translate = new StyleTranslate(new Translate(0, Length.Percent(-50f)));
            bar.style.backgroundColor = lineColor;
            _area.Add(bar);
        }

        /// <summary>Una estación: dot (clickeable) sobre la línea, nombre + chip de tipo debajo, badges de
        /// combinación arriba, y la ficha del jugador si es la parada actual.</summary>
        private void BuildStation(MapNode node, Color lineColor, bool isCurrent, bool isCleared, bool isAvailable)
        {
            Length x = Length.Percent(Mathf.Clamp01(node.x) * 100f);
            Length mid = Length.Percent(50f);

            // ── Dot (el marcador clickeable) ──
            var dot = new Button();
            dot.AddToClassList("map-node");
            dot.AddToClassList(NodeTypeClass(node.type));
            if (isCurrent) dot.AddToClassList("map-node--current");
            else if (isCleared) dot.AddToClassList("map-node--cleared");
            else if (isAvailable) dot.AddToClassList("map-node--available");
            else dot.AddToClassList("map-node--locked");
            dot.style.position = Position.Absolute;
            dot.style.left = x;
            dot.style.top = mid;
            dot.style.translate = new StyleTranslate(new Translate(Length.Percent(-50f), Length.Percent(-50f)));
            dot.style.borderTopColor = dot.style.borderRightColor =
                dot.style.borderBottomColor = dot.style.borderLeftColor = lineColor;

            VisualElement icon = IconLoader.BuildIcon("node-" + node.type.ToString().ToLowerInvariant(), null,
                                                      "map-node__icon");
            if (icon != null) dot.Add(icon);

            if (isCurrent)
            {
                VisualElement ficha = IconLoader.BuildIcon("ficha", "▾", "map-node__ficha");
                if (ficha != null) dot.Add(ficha);
            }

            if (isAvailable && TryGetNodeAction(node.type, node.id, out System.Action onClick))
            {
                dot.clicked += onClick;
            }
            else
            {
                dot.SetEnabled(false);
                dot.pickingMode = PickingMode.Ignore;
            }
            _area.Add(dot);

            // ── Etiqueta (nombre + chip de tipo) debajo del dot ──
            var label = new VisualElement { pickingMode = PickingMode.Ignore };
            label.AddToClassList("map-station__label");
            label.AddToClassList(NodeTypeClass(node.type));   // habilita el color por tipo del chip (USS)
            label.style.position = Position.Absolute;
            label.style.left = x;
            label.style.top = mid;
            label.style.translate = new StyleTranslate(new Translate(Length.Percent(-50f), 0));
            label.style.marginTop = DotOffset;

            var titleEl = new Label(node.title);
            titleEl.AddToClassList("map-node__title");
            label.Add(titleEl);

            string typeName = NodeTypeName(node.type);
            if (!string.IsNullOrEmpty(typeName))
            {
                var typeEl = new Label(typeName);
                typeEl.AddToClassList("map-node__type");
                label.Add(typeEl);
            }
            _area.Add(label);

            // ── Badges de combinación arriba del dot (decoración, spec §17.1) ──
            if (node.combinations != null && node.combinations.Count > 0)
            {
                var combos = new VisualElement { pickingMode = PickingMode.Ignore };
                combos.AddToClassList("map-station__combos");
                combos.style.position = Position.Absolute;
                combos.style.left = x;
                combos.style.top = mid;
                combos.style.translate = new StyleTranslate(new Translate(Length.Percent(-50f), Length.Percent(-100f)));
                combos.style.marginTop = -DotOffset;
                foreach (string letter in node.combinations)
                {
                    var badge = new Label(letter);
                    badge.AddToClassList("map-combo");
                    badge.style.backgroundColor = LineLetterColor(letter);
                    combos.Add(badge);
                }
                _area.Add(combos);
            }
        }

        /// <summary>Color de la línea del mapa desde <see cref="RunMap.lineColorHex"/>; celeste si falta.</summary>
        private static Color LineColor(RunMap map)
        {
            if (!string.IsNullOrEmpty(map.lineColorHex) &&
                ColorUtility.TryParseHtmlString(map.lineColorHex, out Color c))
                return c;
            return new Color(0.11f, 0.66f, 0.79f);   // celeste (Línea A) por defecto
        }

        /// <summary>Color oficial aproximado de cada línea del subte para los badges de combinación.</summary>
        private static Color LineLetterColor(string letter)
        {
            switch (letter)
            {
                case "A": return new Color(0.11f, 0.66f, 0.79f);   // celeste
                case "B": return new Color(0.85f, 0.15f, 0.15f);   // rojo
                case "C": return new Color(0.16f, 0.34f, 0.70f);   // azul
                case "D": return new Color(0.05f, 0.45f, 0.35f);   // verde
                case "E": return new Color(0.45f, 0.20f, 0.55f);   // violeta
                case "H": return new Color(0.95f, 0.78f, 0.10f);   // amarillo
                default:  return new Color(0.40f, 0.43f, 0.50f);
            }
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

        /// <summary>Nombre legible del tipo, para el chip bajo el nombre de la estación. Start sin chip.</summary>
        private static string NodeTypeName(MapNodeType type) => type switch
        {
            MapNodeType.Combat => "Combate",
            MapNodeType.Elite => "Élite",
            MapNodeType.Boss => "Jefe",
            MapNodeType.Shop => "Tienda",
            MapNodeType.Event => "Evento",
            MapNodeType.Workshop => "Taller",
            MapNodeType.Treasure => "Tesoro",
            MapNodeType.Mystery => "Misterio",
            _ => "",
        };

        /// <summary>Acción al clickear un nodo disponible, según su tipo. Devuelve false para tipos aún
        /// no implementados (quedan no-clickeables).</summary>
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
                    onClick = () => EnterScene(nodeId, () => RunSession.Manager.EnterWorkshop(nodeId), "Workshop");
                    return true;
                case MapNodeType.Shop:
                    onClick = () => EnterScene(nodeId, () => RunSession.Manager.EnterShop(nodeId), "Shop");
                    return true;
                case MapNodeType.Event:
                    onClick = () => EnterScene(nodeId, () => RunSession.Manager.EnterEvent(nodeId), "Event");
                    return true;
                default:
                    onClick = null;
                    return false;
            }
        }

        // ── Navegación + feedback ───────────────────────────────────────────────────

        /// <summary>Extension point: feedback al avanzar de parada (audio + FX visual). Hoy un sfx simple;
        /// acá va la animación de la ficha / transición de andén cuando se sumen FX (serán simples).</summary>
        private void PlayStationAdvanceFx(int targetNodeId)
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            // TODO FX: animar la ficha hacia la parada `targetNodeId`, transición de andén, etc.
        }

        /// <summary>Abre la interacción del nodo en el Core y carga su escena (taller/tienda/evento).</summary>
        private void EnterScene(int nodeId, System.Action open, string scene)
        {
            PlayStationAdvanceFx(nodeId);
            open();
            SceneManager.LoadScene(scene);
        }

        private void ChooseNode(int nodeId)
        {
            PlayStationAdvanceFx(nodeId);
            GameEngine engine = RunSession.Manager.BeginCombat(nodeId);
            RunSession.SetPendingCombat(engine);
            SceneManager.LoadScene("Game");
        }

        /// <summary>Tesoro (spec §17.6): da una reliquia (o oro si no quedan) y avanza, sin salir del mapa.</summary>
        private void ChooseTreasure(int nodeId)
        {
            PlayStationAdvanceFx(nodeId);
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

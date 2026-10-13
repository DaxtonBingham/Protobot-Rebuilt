using System.Linq;
using Protobot.Tools;
using Protobot.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.ChainSystem {
    public static class ChainRuntimeBootstrap {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init() {
            var cast = Object.FindObjectOfType<MouseCast>();
            if (cast == null) return;
            var host = ((Component)cast).gameObject;
            var tool = host.GetComponent<InsertChainTool>() ?? host.AddComponent<InsertChainTool>();
            tool.SetMouseCast(cast);
            var toggle = new GameObject("Chain gesture state").AddComponent<ToolToggle>();
            toggle.transform.SetParent(host.transform, false);
            tool.SetToolToggle(toggle);
            var hint = host.GetComponent<ChainGestureHint>() ?? host.AddComponent<ChainGestureHint>();
            hint.Initialize(tool, cast);
        }
    }

    // Uses the existing tooltip and part-row styles, with all placement kept in the viewport.
    public class ChainGestureHint : MonoBehaviour {
        private InsertChainTool tool;
        private MouseCast cast;
        private RectTransform canvas, strip, hoverHint, tether;
        private Text title, shortcuts, hoverText;
        private Text textTemplate;
        private Image tooltipTemplate, buttonTemplate;
        private ColorBlock buttonColors;
        private Button finish, cancel;
        private Button wrapGuide, flipGuide, removeGuide;
        private Text guideCaption;

        public void Initialize(InsertChainTool chainTool, MouseCast mouseCast) { tool = chainTool; cast = mouseCast; }

        private bool EnsureUi() {
            if (canvas != null) return true;
            var tooltip = Resources.FindObjectsOfTypeAll<TooltipUI>().FirstOrDefault(t => t.gameObject.scene.IsValid());
            var top = Resources.FindObjectsOfTypeAll<RectTransform>().FirstOrDefault(t => t.name == "Top Bar" && t.gameObject.scene.IsValid());
            var browser = Resources.FindObjectsOfTypeAll<AddPartsUI>().FirstOrDefault(t => t.gameObject.scene.IsValid());
            if (top == null || tooltip == null || browser == null) return false;
            canvas = top.parent as RectTransform;
            tooltipTemplate = tooltip.GetComponent<Image>();
            textTemplate = tooltip.GetComponentInChildren<Text>(true);
            buttonTemplate = browser.partUI.GetComponent<Image>();
            buttonColors = browser.partUI.GetComponent<Toggle>().colors;
            strip = CreateRect("Chain gesture hint", canvas);
            strip.anchorMin = strip.anchorMax = new Vector2(.5f, 0);
            strip.pivot = new Vector2(.5f, 0);
            strip.anchoredPosition = new Vector2(0, 20);
            strip.sizeDelta = new Vector2(610, 48);
            StyleImage(strip.gameObject.AddComponent<Image>(), tooltipTemplate, false);
            title = Label(strip, "Gesture instruction", 13);
            title.rectTransform.offsetMin = new Vector2(10, 24);
            title.rectTransform.offsetMax = new Vector2(-10, -4);
            shortcuts = Label(strip, "Gesture shortcuts", 11);
            shortcuts.color = new Color(1, 1, 1, .7f);
            shortcuts.rectTransform.offsetMin = new Vector2(10, 4);
            shortcuts.rectTransform.offsetMax = new Vector2(-10, -26);
            finish = ActionButton("Finish", -78, () => tool.EndGesture(true));
            cancel = ActionButton("Cancel", -10, () => tool.EndGesture(false));
            wrapGuide = GuideButton("Other run (R)", 83, 114, () => tool.MoveSelectedGuideToOtherRun());
            flipGuide = GuideButton("Flip contact (F)", 204, 105, () => tool.FlipSelectedGuideSide());
            removeGuide = GuideButton("Remove", 316, 68, () => tool.RemoveGuideAt(tool.SelectedEndpointIndex));
            guideCaption = Label(strip, "Tensioner controls label", 11);
            guideCaption.text = "Tensioner"; guideCaption.alignment = TextAnchor.MiddleLeft;
            guideCaption.rectTransform.offsetMin = new Vector2(10, 3);
            guideCaption.rectTransform.offsetMax = new Vector2(-530, -26);
            hoverHint = CreateRect("Chain hover hint", canvas);
            hoverHint.sizeDelta = new Vector2(200, 23); hoverHint.pivot = new Vector2(0,1);
            StyleImage(hoverHint.gameObject.AddComponent<Image>(), tooltipTemplate, false);
            hoverText = Label(hoverHint, "Hover instruction", textTemplate.fontSize);
            tether = CreateRect("Chain pointer preview", canvas); tether.pivot = new Vector2(0,.5f);
            var line = tether.gameObject.AddComponent<Image>(); line.color = new Color(1,1,1,.65f); line.raycastTarget = false;
            tether.SetAsFirstSibling();
            return true;
        }

        private Button ActionButton(string text, float x, UnityEngine.Events.UnityAction action) {
            var rect = CreateRect(text + " chain", strip);
            rect.anchorMin = rect.anchorMax = new Vector2(1,1); rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(x,-3); rect.sizeDelta = new Vector2(62,22);
            var image = rect.gameObject.AddComponent<Image>(); StyleImage(image, buttonTemplate, true);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.colors = buttonColors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(action);
            Label(rect, text + " label", 12).text = text;
            return button;
        }

        private Button GuideButton(string text, float x, float width, UnityEngine.Events.UnityAction action) {
            var button = ActionButton(text, 0, action);
            button.name = text + " tensioner";
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, 3); rect.sizeDelta = new Vector2(width, 20);
            return button;
        }

        private void Update() {
            if (tool == null || !EnsureUi()) return;
            bool blocked = Protobot.CustomParts.CustomPartStudioController.IsStudioOpen || !canvas.gameObject.activeInHierarchy;
            bool active = tool.GestureActive && !blocked;
            strip.gameObject.SetActive(!blocked && (active || !string.IsNullOrEmpty(tool.InteractionHint)));
            hoverHint.gameObject.SetActive(false); tether.gameObject.SetActive(false);
            if (blocked) return;
            bool fromBrowser = active && tool.BrowserPlacement;
            finish.gameObject.SetActive(fromBrowser); cancel.gameObject.SetActive(fromBrowser);
            finish.interactable = tool.HasValidDraft;
            title.rectTransform.offsetMax = new Vector2(fromBrowser ? -150 : -10, -4);
            title.alignment = fromBrowser ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            var selectedGuide = active ? tool.SelectedGuide : null;
            wrapGuide.gameObject.SetActive(selectedGuide != null);
            flipGuide.gameObject.SetActive(selectedGuide != null);
            removeGuide.gameObject.SetActive(selectedGuide != null);
            guideCaption.gameObject.SetActive(selectedGuide != null);
            shortcuts.gameObject.SetActive(selectedGuide == null);
            if (selectedGuide != null) {
                flipGuide.interactable = true;
                flipGuide.GetComponentInChildren<Text>().text = "Flip contact (F)";
            }
            if (active) {
                string size = new[] { "Auto size", "0.148 in", "0.250 in", "0.385 in" }[tool.GetToolbarStandardIndex()];
                title.text = !string.IsNullOrEmpty(tool.InteractionHint) ? tool.InteractionHint
                    : !string.IsNullOrEmpty(tool.CurrentValidationMessage) ? tool.CurrentValidationMessage
                    : tool.PendingEndpoints.Count == 0 ? "Chain: click the first sprocket"
                    : tool.PendingEndpoints.Count == 1 ? "Add a sprocket or any part as a tensioner"
                    : fromBrowser ? "Click parts to add tensioners, or Enter to finish"
                    : "Click any part for a tensioner; release C to finish";
                shortcuts.text = size + " (Tab)    Align " + (tool.AutoAlign ? "on" : "off") + " (A)    Backspace: remove last    Shift-click: remove    Esc: cancel";
            } else {
                title.text = tool.InteractionHint;
                shortcuts.text = "Motion > Chain, or hold C and click sprockets";
            }
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (overUi || Mouse.current == null) return;
            var hovered = cast.overObj ? cast.gameObject : null;
            if (hovered != null) {
                var part = ChainSprocketUtility.ResolvePartObject(hovered);
                var connection = hovered.GetComponentInParent<ChainConnection>();
                var guide = part != null ? ChainGuideRuntimeAuthoring.GetRuntimeGuide(part) : null;
                bool sprocket = part != null && ChainSprocketUtility.IsSprocket(part);
                bool selected = part != null && tool.PendingEndpoints.Any(e => e != null && e.gameObject == part);
                bool first = part != null && tool.PendingEndpoints.Count > 0 && tool.PendingEndpoints[0].gameObject == part;
                if (active || sprocket || connection != null) {
                    if (!active) hoverText.text = "Hold C + click to add or edit chain";
                    else if (connection != null && tool.PendingEndpoints.Count == 0) hoverText.text = "Click to edit chain";
                    else if (fromBrowser && first && tool.HasValidDraft) hoverText.text = "Click to finish chain";
                    else if (guide != null && tool.PendingEndpoints.Any(e => e != null && e.gameObject == guide.gameObject)) hoverText.text = "Click to adjust tensioner; Shift-click to remove";
                    else if (sprocket && selected) hoverText.text = "Selected; Shift-click to remove";
                    else if (sprocket) hoverText.text = "Click to add sprocket";
                    else if (tool.PendingEndpoints.Count >= 1 && part != null && part.GetComponent<SavedObject>() != null) hoverText.text = "Click to tension the nearest chain run";
                    else hoverText.text = "Choose a sprocket";
                    hoverHint.sizeDelta = new Vector2(hoverText.preferredWidth + 16, 23);
                    hoverHint.gameObject.SetActive(true);
                    var canvasComponent = canvas.GetComponent<Canvas>();
                    Vector2 local;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, Mouse.current.position.ReadValue(), canvasComponent.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvasComponent.worldCamera, out local);
                    local += new Vector2(18, -18);
                    local.x = Mathf.Clamp(local.x, canvas.rect.xMin + 8, canvas.rect.xMax - hoverHint.rect.width - 8);
                    local.y = Mathf.Clamp(local.y, canvas.rect.yMin + 32, canvas.rect.yMax - 8);
                    hoverHint.localPosition = local;
                }
            }
            ChainPathSolver.Span hoveredSpan = default;
            bool showRun = active && tool.TryGetHoveredGuideablePart(out var guidePart, out var anchor)
                && tool.TryGetNearestSpan(anchor, out hoveredSpan);
            if (active && Camera.main != null && (tool.PendingEndpoints.Count == 1 || showRun)) {
                Vector3 screen = Camera.main.WorldToScreenPoint(showRun ? hoveredSpan.start : tool.PendingEndpoints[0].WorldCenter);
                Vector3 endScreen = showRun ? Camera.main.WorldToScreenPoint(hoveredSpan.end) : (Vector3)Mouse.current.position.ReadValue();
                if (screen.z <= 0 || (showRun && endScreen.z <= 0)) return;
                var cc = canvas.GetComponent<Canvas>(); var camera = cc.renderMode == RenderMode.ScreenSpaceOverlay ? null : cc.worldCamera;
                Vector2 from, to;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, camera, out from);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, endScreen, camera, out to);
                tether.gameObject.SetActive(true); tether.localPosition = from;
                Vector2 delta = to - from;
                tether.sizeDelta = new Vector2(delta.magnitude, showRun ? 2 : 1); tether.localRotation = Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
            }
        }

        private static void StyleImage(Image image, Image template, bool raycast) {
            image.sprite = template.sprite; image.type = template.type; image.color = template.color;
            image.pixelsPerUnitMultiplier = template.pixelsPerUnitMultiplier; image.raycastTarget = raycast;
        }

        private static RectTransform CreateRect(string name, Transform parent) {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect;
        }

        private Text Label(Transform parent, string name, int size) {
            var rect = CreateRect(name, parent); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8, 0); rect.offsetMax = new Vector2(-8, 0);
            var text = rect.gameObject.AddComponent<Text>(); text.font = textTemplate.font; text.fontSize = size; text.color = textTemplate.color;
            text.alignment = TextAnchor.MiddleCenter; text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false;
            return text;
        }
    }
}

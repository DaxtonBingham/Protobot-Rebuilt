using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Protobot.Builds;
using Protobot.ChainSystem;
using Protobot.SelectionSystem;
using Protobot.UI;
using SFB;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace Protobot.Export {
    /// <summary>Native export commands with background STEP and STL writers.</summary>
    public sealed class StepExportController : MonoBehaviour {
        [Serializable] sealed class ProgressMessage {
            public string status, message, path;
            public float progress;
            public int parts, total, definitions, solidInstances;
        }
        Process process;
        readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
        readonly List<Button> commands = new List<Button>();
        RectTransform fileMenu;
        ExportSubmenu exportMenu;
        RectTransform panel;
        Text status;
        Image progress;
        Button action, details;
        Text actionLabel;
        string jobDirectory, jobPath, outputPath, detailsPath;
        volatile bool cancelled;
        string activeFormat = "STEP";
        public bool IsExporting { get; private set; }
        public string LastStatus { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() {
            if (FindObjectOfType<StepExportController>() != null) return;
            var manager = FindObjectOfType<BuildsManager>();
            if (manager != null) manager.gameObject.AddComponent<StepExportController>();
        }

        void Start() {
            var menu = Resources.FindObjectsOfTypeAll<RectTransform>()
                .FirstOrDefault(t => t.name == "File Menu" && t.gameObject.scene.IsValid());
            if (menu == null) { Debug.LogWarning("Export could not locate the File menu."); return; }
            var source = menu.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Save As Button")
                ?? menu.GetComponentsInChildren<Button>(true).FirstOrDefault();
            if (source == null) return;
            fileMenu = menu;
            var launcher = MenuButton(menu, source, "Export As...", () => exportMenu.Show(true), true);
            commands.Add(launcher);
            var divider = menu.Cast<Transform>().FirstOrDefault(t => t.name == "Divider")
                ?? menu.Cast<Transform>().FirstOrDefault(t => t.name.IndexOf("Exit", StringComparison.OrdinalIgnoreCase) >= 0);
            if (divider != null) launcher.transform.SetSiblingIndex(divider.GetSiblingIndex());
            menu.sizeDelta = new Vector2(Mathf.Max(menu.sizeDelta.x, 160), menu.sizeDelta.y + 20);
            CreateExportMenu(launcher, menu, source);
            CreateStatusPanel(menu.GetComponentInParent<Canvas>().transform, source);
        }

        void CreateExportMenu(Button launcher, RectTransform menu, Button source) {
            exportMenu = launcher.gameObject.AddComponent<ExportSubmenu>();
            var label = launcher.GetComponentInChildren<Text>();
            label.rectTransform.offsetMax = new Vector2(-22, 0);
            var arrow = Label(launcher.transform, label, "> ");
            arrow.alignment = TextAnchor.MiddleRight;
            arrow.rectTransform.offsetMin = new Vector2(0, 0); arrow.rectTransform.offsetMax = new Vector2(-6, 0);
            var parentCanvas = menu.GetComponentInParent<Canvas>();
            // Keep the flyout outside the File menu's rounded clipping mask.
            var popup = new GameObject("Export options", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            popup.SetParent(parentCanvas.transform, false); popup.gameObject.layer = menu.gameObject.layer;
            popup.anchorMin = popup.anchorMax = new Vector2(0, 1); popup.pivot = new Vector2(0, 1);
            popup.sizeDelta = new Vector2(110, 44);
            var owner = FindObjectsOfType<MenuBarToggle>().FirstOrDefault(t => t.Menu == menu.gameObject);
            if (owner != null) owner.Flyout = popup;
            var events = popup.gameObject.AddComponent<EventTrigger>();
            var leave = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            leave.callback.AddListener(data => exportMenu.OnPointerExit((PointerEventData)data)); events.triggers.Add(leave);
            var original = menu.GetComponent<Image>(); var background = popup.GetComponent<Image>();
            background.sprite = original.sprite; background.type = original.type; background.color = original.color;
            background.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier;
            var layout = popup.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(2, 2, 2, 2);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var step = MenuButton(popup, source, "STEP...", () => ChooseExport(false));
            commands.Add(step);
            commands.Add(MenuButton(popup, source, "STL...", () => ChooseExport(true)));
            exportMenu.Initialize(popup, step);
        }

        void ChooseExport(bool stl) {
            exportMenu.Close();
            foreach (var menuToggle in FindObjectsOfType<MenuBarToggle>()) menuToggle.GetComponent<Toggle>().isOn = false;
            fileMenu.gameObject.SetActive(false);
            BeginExportDialog(false, stl);
        }

        static Button MenuButton(Transform parent, Button source, string label, UnityEngine.Events.UnityAction click, bool fileCommand = false) {
            var rect = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.sizeDelta = new Vector2(190, 20);
            var layout = rect.gameObject.AddComponent<LayoutElement>(); layout.preferredHeight = 20; layout.minHeight = 20;
            var image = rect.gameObject.AddComponent<Image>();
            var original = source.GetComponent<Image>();
            if (original != null) { image.sprite = original.sprite; image.type = original.type; image.color = original.color; image.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier; }
            else image.color = new Color(.16f, .16f, .16f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image; button.colors = source.colors;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(click);
            var text = Label(rect, source.GetComponentInChildren<Text>(true), label);
            text.rectTransform.offsetMin = new Vector2(fileCommand ? 30 : 8, 0); text.rectTransform.offsetMax = new Vector2(-6, 0);
            text.alignment = TextAnchor.MiddleLeft;
            if (fileCommand) {
                var sourceIcon = source.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.gameObject != source.gameObject);
                if (sourceIcon != null) {
                    var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    icon.transform.SetParent(rect, false); icon.sprite = sourceIcon.sprite; icon.color = sourceIcon.color;
                    icon.preserveAspect = true; icon.raycastTarget = false;
                    icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f);
                    icon.rectTransform.anchoredPosition = new Vector2(15, 0); icon.rectTransform.sizeDelta = new Vector2(14, 14);
                }
            }
            return button;
        }

        static Text Label(Transform parent, Text template, string value) {
            var text = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.font = template != null ? template.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = template != null ? template.fontSize : 13;
            text.color = template != null ? template.color : Color.white;
            text.raycastTarget = false; text.text = value;
            return text;
        }

        void CreateStatusPanel(Transform canvas, Button source) {
            panel = new GameObject("Export status", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            panel.SetParent(canvas, false); panel.anchorMin = panel.anchorMax = new Vector2(.5f, 0);
            panel.pivot = new Vector2(.5f, 0); panel.anchoredPosition = new Vector2(0, 24); panel.sizeDelta = new Vector2(580, 66);
            panel.GetComponent<Image>().color = new Color(.13f, .13f, .13f, .98f);
            status = Label(panel, source.GetComponentInChildren<Text>(true), "");
            status.alignment = TextAnchor.MiddleLeft; status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.rectTransform.offsetMin = new Vector2(12, 12); status.rectTransform.offsetMax = new Vector2(-164, -8);
            progress = new GameObject("Progress", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            progress.transform.SetParent(panel, false); progress.rectTransform.anchorMin = progress.rectTransform.anchorMax = Vector2.zero;
            progress.rectTransform.pivot = Vector2.zero; progress.rectTransform.anchoredPosition = new Vector2(12, 7);
            progress.color = new Color(.6f, .6f, .6f); progress.raycastTarget = false;
            action = MenuButton(panel, source, "Cancel", CancelOrClose);
            var rect = (RectTransform)action.transform; rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
            rect.pivot = new Vector2(1, .5f); rect.anchoredPosition = new Vector2(-12, 0); rect.sizeDelta = new Vector2(68, 24);
            actionLabel = action.GetComponentInChildren<Text>();
            details = MenuButton(panel, source, "Details", ShowDetails);
            rect = (RectTransform)details.transform; rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
            rect.pivot = new Vector2(1, .5f); rect.anchoredPosition = new Vector2(-88, 0); rect.sizeDelta = new Vector2(66, 24);
            details.gameObject.SetActive(false); panel.gameObject.SetActive(false);
        }

        public void BeginExport(bool selectionOnly) {
            BeginExportDialog(selectionOnly, false);
        }

        public void BeginStlExport(bool selectionOnly) {
            BeginExportDialog(selectionOnly, true);
        }

        void BeginExportDialog(bool selectionOnly, bool stl) {
            if (IsExporting) return;
            var manager = GetComponent<BuildsManager>();
            string title = Path.GetFileNameWithoutExtension(manager != null ? manager.buildPath : "");
            if (string.IsNullOrWhiteSpace(title)) title = "Protobot assembly";
            var selectedParts = new HashSet<SavedObject>(); var selectedChains = new HashSet<ChainConnection>();
            if (selectionOnly) {
                CollectSelection(selectedParts, selectedChains);
                if (selectedParts.Count == 0 && selectedChains.Count == 0) { ShowStatus("Select the parts you want to export first.", 0, false); return; }
            }
            string format = stl ? "STL" : "STEP";
            string path = StandaloneFileBrowser.SaveFilePanel((selectionOnly ? "Export Selection as " : "Export Assembly as ") + format + (stl ? " (millimeters)" : ""), "", title, stl ? "stl" : "step");
            if (string.IsNullOrEmpty(path)) return;
            string extension = Path.GetExtension(path);
            if (!(stl ? extension.Equals(".stl", StringComparison.OrdinalIgnoreCase)
                : extension.Equals(".step", StringComparison.OrdinalIgnoreCase) || extension.Equals(".stp", StringComparison.OrdinalIgnoreCase))) {
                ShowStatus("Use a ." + (stl ? "stl" : "step") + " filename for this export.", 0, false); return;
            }
            StartCoroutine(Export(path, title, selectionOnly ? selectedParts : null, selectionOnly ? selectedChains : null));
        }

        internal static void CollectSelection(HashSet<SavedObject> selectedParts, HashSet<ChainConnection> selectedChains) {
            // Hover selection has a separate manager; only committed selections
            // from the main selection manager should enter the export.
            var selections = FindObjectsOfType<SelectionManager>().Where(m => m.GetComponent<HoverSelector>() == null && m.current != null
                && !(m.current is HoleSelection) && !(m.current.selector is HoverSelector));
            foreach (var managerSelection in selections) {
                var multi = managerSelection.current as MultiSelection;
                var objects = multi != null ? multi.objs : new List<GameObject> { managerSelection.current.gameObject };
                foreach (var obj in objects) {
                    if (obj == null) continue;
                    foreach (var part in obj.GetComponentsInChildren<SavedObject>()) selectedParts.Add(part);
                    foreach (var chain in obj.GetComponentsInChildren<ChainConnection>()) selectedChains.Add(chain);
                }
            }
        }

        IEnumerator Export(string path, string title, IEnumerable<SavedObject> selectedParts, IEnumerable<ChainConnection> selectedChains) {
            IsExporting = true; cancelled = false; outputPath = path; detailsPath = null; jobPath = null; jobDirectory = null;
            bool stl = Path.GetExtension(path).Equals(".stl", StringComparison.OrdinalIgnoreCase);
            activeFormat = stl ? "STL" : "STEP";
            while (messages.TryDequeue(out _)) { }
            foreach (var button in commands) button.interactable = false;
            ShowStatus("Preparing " + activeFormat + " assembly...", 0, true);
            yield return null;
            if (cancelled) {
                IsExporting = false;
                foreach (var button in commands) button.interactable = true;
                ShowStatus(activeFormat + " export cancelled.", 0, false);
                yield break;
            }
            string failure = null;
            Task<StlExportWriter.Result> stlTask = null;
            int stlCompleted = 0, stlTotal = 0;
            try {
                jobDirectory = Path.Combine(Application.temporaryCachePath, "StepExport", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(jobDirectory);
                var snapshot = StepExportSnapshot.Capture(title, selectedParts, selectedChains);
                if (stl) {
                    stlTotal = snapshot.parts.Count;
                    stlTask = Task.Run(() => StlExportWriter.Write(snapshot, path, () => cancelled,
                        count => Interlocked.Exchange(ref stlCompleted, count)));
                } else {
                    string runtime = Path.Combine(Application.streamingAssetsPath, "StepExport");
                    string executable = Path.Combine(runtime, "Protobot CAD.exe");
                    if (!File.Exists(executable)) throw new FileNotFoundException("The CAD exporter is missing. Install the complete Protobot build.");
                    jobPath = Path.Combine(jobDirectory, "assembly.json");
                    snapshot.Write(jobPath);
                    process = new Process { StartInfo = new ProcessStartInfo {
                        FileName = executable, Arguments = "--job " + Quote(jobPath) + " --library " + Quote(Path.Combine(runtime, "Library")) + " --output " + Quote(path) + " --cleanup-job",
                        UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = runtime
                    }};
                    process.OutputDataReceived += (_, e) => { if (e.Data != null) messages.Enqueue(e.Data); };
                    process.ErrorDataReceived += (_, e) => { if (e.Data != null) messages.Enqueue(e.Data); };
                    if (!process.Start()) throw new InvalidOperationException("The CAD exporter could not start.");
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                }
            } catch (Exception ex) {
                failure = ex.Message;
                if (process != null) {
                    try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                    process.Dispose(); process = null;
                }
            }
            ProgressMessage final = null;
            if (failure == null && stl) {
                while (!stlTask.IsCompleted) {
                    int done = Volatile.Read(ref stlCompleted);
                    if (!cancelled) ShowStatus("Writing STL (" + done + "/" + stlTotal + ")...", (float)done / stlTotal, true);
                    yield return new WaitForSecondsRealtime(.1f);
                }
                if (stlTask.IsFaulted) {
                    var error = stlTask.Exception.GetBaseException();
                    if (error is OperationCanceledException) final = new ProgressMessage { status = "cancelled" };
                    else failure = error.Message;
                } else if (stlTask.IsCanceled) final = new ProgressMessage { status = "cancelled" };
                else {
                    var result = stlTask.Result;
                    final = new ProgressMessage { status = "complete", parts = result.Parts };
                }
            } else if (failure == null) {
                while (!process.HasExited) {
                    ReadProgress(ref final);
                    yield return new WaitForSecondsRealtime(.1f);
                }
                process.WaitForExit(); ReadProgress(ref final);
                if (process.ExitCode != 0 && (final == null || final.status != "cancelled"))
                    failure = final != null && !string.IsNullOrEmpty(final.message) ? final.message : "The CAD exporter stopped before completing the file. See details.";
                else if (final == null || final.status != "complete") {
                    if (!cancelled && (final == null || final.status != "cancelled")) failure = "The CAD exporter did not confirm completion. See details.";
                }
                process.Dispose(); process = null;
            }
            IsExporting = false;
            foreach (var button in commands) button.interactable = true;
            if (failure != null) {
                if (!string.IsNullOrEmpty(jobDirectory)) {
                    detailsPath = Path.Combine(jobDirectory, "Export details.txt");
                    AppendDetail(failure);
                }
                ShowStatus(failure.Length > 170 ? activeFormat + " export could not finish. See details for the parts that need attention." : failure, 0, false);
            } else if (final == null || final.status != "complete") ShowStatus(activeFormat + " export cancelled.", 0, false);
            else ShowStatus("Exported " + final.parts + " components to " + Path.GetFileName(outputPath), 1, false);
            // Snapshots can be large. Keep the small diagnostic log, not a second
            // copy of every source mesh after an export has finished.
            try { if (!string.IsNullOrEmpty(jobPath) && File.Exists(jobPath)) File.Delete(jobPath); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        void AppendDetail(string line) {
            if (string.IsNullOrEmpty(jobDirectory)) return;
            try { File.AppendAllText(Path.Combine(jobDirectory, "Export details.txt"), line + "\n"); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        void ReadProgress(ref ProgressMessage final) {
            string line;
            while (messages.TryDequeue(out line)) {
                AppendDetail(line);
                if (!line.StartsWith("{")) continue;
                ProgressMessage update;
                try { update = JsonUtility.FromJson<ProgressMessage>(line); } catch { continue; }
                if (update == null || string.IsNullOrEmpty(update.status)) continue;
                if (update.status == "complete" || update.status == "error" || update.status == "cancelled") final = update;
                else if (!cancelled) ShowStatus(update.status + (update.total > 0 ? " (" + update.parts + "/" + update.total + ")" : "..."), update.progress, true);
            }
        }

        void ShowStatus(string message, float fraction, bool running) {
            LastStatus = message;
            if (panel == null) return;
            panel.gameObject.SetActive(true); panel.SetAsLastSibling();
            status.text = message; progress.rectTransform.sizeDelta = new Vector2(556 * Mathf.Clamp01(fraction), 2);
            actionLabel.text = running ? "Cancel" : "Close";
            action.interactable = !cancelled || !running;
            details.gameObject.SetActive(!running && !string.IsNullOrEmpty(detailsPath));
        }

        public void CancelOrClose() {
            if (!IsExporting) { if (panel != null) panel.gameObject.SetActive(false); return; }
            cancelled = true;
            RequestCancellation();
            ShowStatus("Cancelling " + activeFormat + " export...", 0, true);
        }
        void ShowDetails() {
            try {
                if (!string.IsNullOrEmpty(detailsPath) && File.Exists(detailsPath))
                    Process.Start(new ProcessStartInfo(detailsPath) { UseShellExecute = true });
            } catch (Exception ex) { ShowStatus("Could not open export details: " + ex.Message, 0, false); }
        }
        void RequestCancellation() {
            if (string.IsNullOrEmpty(jobPath)) return;
            try { File.WriteAllText(Path.ChangeExtension(jobPath, ".cancel"), "cancel"); }
            catch (Exception ex) {
                AppendDetail("Cancellation signal failed: " + ex.Message);
                try { if (process != null && !process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            }
        }
        internal static string Quote(string argument) {
            // CommandLineToArgvW quoting, including embedded quotes and trailing backslashes.
            var result = new System.Text.StringBuilder("\""); int slashes = 0;
            foreach (char c in argument) {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2); return result.Append('"').ToString();
        }
        void OnDestroy() {
            cancelled = true;
            if (process == null) return;
            // Let the isolated writer remove its temporary file and snapshot.
            // It checks cancellation before atomically publishing any output.
            RequestCancellation();
            process.Dispose(); process = null;
        }
    }
}

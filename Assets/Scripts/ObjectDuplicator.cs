using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Protobot.StateSystems;
using Protobot.InputEvents;
using Protobot.Outlining;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System;
using System.Linq;
using Protobot.ChainSystem;

namespace Protobot {
    public class ObjectDuplicator : MonoBehaviour {
        [SerializeField] private MovementManager movementManager;
        [SerializeField] private InputEvent input;

        private List<GameObject> prevDuplicatedObjs = new List<GameObject>();
        private bool duplicatedOnMove;

        public void Awake() {
            movementManager.OnStartMoving += () => {
                if (input.IsPressed) {
                    if (input.IsKeyPressed("Tab") && input.GetCurrentKeybind() == "Alt") {
                        print("ohyeahj");
                        return;
                    }
                    DuplicateObject();
                    
                    foreach (GameObject obj in prevDuplicatedObjs) {
                        ObjectElement objElement = new ObjectElement(obj);
                        objElement.existing = false;

                        StateSystem.AddElement(objElement);
                    }

                    duplicatedOnMove = true;
                }
            };

            movementManager.OnMovementRecorded += () => {
                if (duplicatedOnMove) {
                    ObjectElement.AddObjectElements(prevDuplicatedObjs);
                    duplicatedOnMove = false;
                }
            };
        }

        private void DuplicateObject() {
            prevDuplicatedObjs = new List<GameObject>();
            if (movementManager.MovingObj == null) return;
            // A selection pivot contains its parts. Cloning both the pivot and each
            // child created an extra set of copies; only copy document parts once.
            var sources = movementManager.MovingObj.GetConnectedObjects(true, true)
                .Where(obj => obj != null)
                .SelectMany(obj => obj.GetComponentsInChildren<SavedObject>())
                .Where(view => view.gameObject.activeInHierarchy)
                .Select(view => view.gameObject).Distinct().ToList();
            var indices = sources.Select((obj, index) => new { obj, index }).ToDictionary(item => item.obj, item => item.index);
            var chainData = ChainManager.ExportBuildData(obj => indices.TryGetValue(obj, out int index) ? index : -1);
            var previousChains = new HashSet<ChainConnection>(ChainManager.Connections);
            try {
                foreach (GameObject obj in sources) {
                    var clone = Instantiate(obj, obj.transform.position, obj.transform.rotation);
                    prevDuplicatedObjs.Add(clone);
                    clone.DisableOutline();
                    var renderer = clone.GetComponent<Renderer>();
                    if (renderer != null && renderer.sharedMaterial != null) renderer.material = new Material(renderer.sharedMaterial);
                    var view = clone.GetComponent<SavedObject>();
                    if (!string.IsNullOrEmpty(view.customInstanceId)) {
                        view.customInstanceId = Guid.NewGuid().ToString("N");
                        RobotDocument.Synchronize(view, PartChange.Metadata);
                    }
                }
                // Recreate bindings against the copies. Instantiating the live chain
                // copied incomplete runtime state and kept references to old sprockets.
                ChainManager.LoadBuildData(chainData, index => prevDuplicatedObjs[index]);
                prevDuplicatedObjs.AddRange(ChainManager.Connections.Where(chain => !previousChains.Contains(chain)).Select(chain => chain.gameObject));
            } catch (Exception ex) {
                foreach (var chain in ChainManager.Connections.Where(chain => !previousChains.Contains(chain)).ToArray()) {
                    chain.gameObject.SetActive(false); Destroy(chain.gameObject);
                }
                foreach (var clone in prevDuplicatedObjs) { clone.SetActive(false); Destroy(clone); }
                prevDuplicatedObjs.Clear();
                FindObjectOfType<Protobot.UI.FeedbackDisplay>()?.ShowAlert("Couldn't duplicate this selection: " + ex.Message);
            }
        }
    }
}

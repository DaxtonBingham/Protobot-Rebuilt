using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using System.Linq;

namespace Protobot.Tools {
    public class NewMoveShaftTool : MonoBehaviour {
        [SerializeField] private ObjectLink shaftObjLink;
        private Vector3 shaftObjForward => shaftObjLink.tform.forward;

        private GameObject curShaft;

        [SerializeField] private ObjectLink targetObjLink;
        [SerializeField] private HoleFace targetHoleFace;

        [SerializeField] private Transform buttonCanvas;

        [SerializeField] private Transform positiveArrow;
        [SerializeField] private Transform negativeArrow;

        [SerializeField] private Transform positiveBracket;
        [SerializeField] private Transform negativeBracket;

        [SerializeField] private Pivot moveShaftPivot;
        
        public void OnSetShaftObj() {
            float shaftLength = GetShaftLength();

            buttonCanvas.forward = shaftObjForward;

            positiveArrow.transform.localPosition = Vector3.up * shaftLength;
            negativeArrow.transform.localPosition = Vector3.down * shaftLength;

            ResetPivot();
            moveShaftPivot.transform.position = shaftObjLink.tform.position;
            moveShaftPivot.transform.forward = shaftObjForward;
            moveShaftPivot.AddObject(shaftObjLink.obj);
        }

        public void SetCurShaft(GameObject newShaft) {
            curShaft = newShaft;
        }

        public float GetShaftLength() => shaftObjLink.obj.GetComponent<MeshFilter>().sharedMesh.bounds.extents.z;
        public Vector3 GetShaftExtents() => shaftObjLink.obj.GetComponent<MeshCollider>().bounds.extents;

        //<Summary> Returns the next object on the shaft in a certain direction from a starting position </Summary>
        public GameObject GetNextObjOnShaft(Vector3 startPos, Vector3 dir) {
            return HoleWorld.Raycast(new Ray(startPos, dir), out var hit) ? hit.hole.holeData.part : null;
        }

        public void SetPivotObjects(Vector3 startPos, Vector3 endPos) {
            ResetPivot();
            moveShaftPivot.transform.position = shaftObjLink.tform.position;
            moveShaftPivot.transform.forward = shaftObjForward;

            var objectsWithin = HoleWorld.PartsAlongSegment(startPos, endPos, .01f);

            moveShaftPivot.AddObjects(objectsWithin);
            moveShaftPivot.AddObject(shaftObjLink.obj);

            AddPivotOutline();
        }

        private void ClearPivotOutline() {
            moveShaftPivot.objects.
                Where(x => x != shaftObjLink.obj).
                ToList();
        }

        private void AddPivotOutline() {
            moveShaftPivot.objects.
                Where(x => x != shaftObjLink.obj).
                ToList();
        }

        public void ResetPivot() {
            ClearPivotOutline();
            moveShaftPivot.SetEmpty();
        }

        public void AddNextUp() {
            var nextObject = GetNextObjOnShaft(positiveBracket.position, shaftObjForward);
            if (nextObject == null) return;
            var nextObjPos = nextObject.transform.position;
            positiveBracket.DOMove(nextObjPos, 0.25f);

            SetPivotObjects(nextObjPos, negativeBracket.position);
        }

        public void RemoveNextDown() {
            var nextObject = GetNextObjOnShaft(positiveBracket.position, -shaftObjForward);
            if (nextObject == null) return;
            var nextObjPos = nextObject.transform.position;
            positiveBracket.DOMove(nextObjPos, 0.25f);

            SetPivotObjects(nextObjPos, negativeBracket.position);
        }

        public void AddNextDown() {
            var nextObject = GetNextObjOnShaft(negativeBracket.position, -shaftObjForward);
            if (nextObject == null) return;
            var nextObjPos = nextObject.transform.position;
            negativeBracket.DOMove(nextObjPos, 0.25f);

            SetPivotObjects(nextObjPos, positiveBracket.position);
        }

        public void RemoveNextUp() {
            var nextObject = GetNextObjOnShaft(negativeBracket.position, shaftObjForward);
            if (nextObject == null) return;
            var nextObjPos = nextObject.transform.position;
            negativeBracket.DOMove(nextObjPos, 0.25f);

            SetPivotObjects(nextObjPos, positiveBracket.position);
        }


        public void SetShaftPosition(bool reverseDir) {
            SavedObject savedObj = targetObjLink?.obj?.GetComponent<SavedObject>();

            if (savedObj != null && savedObj.nameId == "MOTR") {
                Transform motrTransform = targetObjLink.tform;

                curShaft.transform.forward = motrTransform.forward;

                float shaftLength = GetShaftLength();
                
                Vector3 newPos = motrTransform.position + motrTransform.right * -0.5f + (motrTransform.forward * (shaftLength + 0.85f));

                curShaft.transform.DOMove(newPos, 0.25f);
            }
            else if (targetHoleFace.gameObject.activeInHierarchy) {
                curShaft.transform.DOMove(targetHoleFace.position, 0.25f);
                curShaft.transform.forward = targetHoleFace.direction;
            }

            if (reverseDir)
                curShaft.transform.forward = -curShaft.transform.forward;
        }
    }
}

using System.Collections;
using UnityEngine;
using EPOOutline;

namespace Protobot.Outlining {
    public static class Outliner {
        public static void EnableOutline(this GameObject obj, int colorIndex, int layer = 0, float fillAlpha = 0.1f) {
            if (obj.GetComponent<ShadowRenderProxy>() != null) return;
            obj.GetComponent<Protobot.ChainSystem.ChainInstances>()?.SetOutlined(true);
            RobotRenderer.SetOutlined(obj, true);
            Renderer renderer = obj.GetComponent<Renderer>();

            if (renderer != null) {
                Outlinable outline = obj.GetComponent<Outlinable>();

                if (outline == null) {
                    outline = obj.AddComponent<Outlinable>();
                    ViewportPresentation.Changed();
                    outline.AddAllChildRenderersToRenderingList();
                    for (int i = outline.OutlineTargets.Count - 1; i >= 0; i--)
                        if (outline.OutlineTargets[i].Renderer.GetComponent<ShadowRenderProxy>() != null) outline.RemoveTarget(outline.OutlineTargets[i]);
                    //outline.OutlineParameters.FillPass.Shader = Resources.Load<Shader>("Easy performant Outline/Shaders/Fills/ColorFill");
                }

                var color = OutlineSettings.GetColor(colorIndex);
                if (!outline.enabled || outline.OutlineLayer != layer || outline.OutlineParameters.Color != color) ViewportPresentation.Changed();
                outline.enabled = true;
                outline.OutlineLayer = layer;
                outline.OutlineParameters.Color = color;

                //var fillColor = OutlineSettings.GetColor(colorIndex);
                //fillColor.a = fillAlpha;
                //outline.OutlineParameters.FillPass.SetColor("_PublicColor", fillColor);
            }

            for (int i = 0; i < obj.transform.childCount; i++)
                EnableOutline(obj.transform.GetChild(i).gameObject, colorIndex);
        }

        public static void DisableOutline(this GameObject obj) {
            if (obj.GetComponent<ShadowRenderProxy>() != null) return;
            obj.GetComponent<Protobot.ChainSystem.ChainInstances>()?.SetOutlined(false);
            RobotRenderer.SetOutlined(obj, false);
            Outlinable outline = obj.GetComponent<Outlinable>();

            if (outline != null) {
                if (outline.enabled) ViewportPresentation.Changed();
                //if (outline.persistent)
                //    return;
                //else
                    outline.enabled = false;
            }

            for (int i = 0; i < obj.transform.childCount; i++)
                DisableOutline(obj.transform.GetChild(i).gameObject);
        }
    }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UVTool.Editor
{
    [CustomEditor(typeof(UVInspectorTool))]
    public class UVInspectorToolEditor : UnityEditor.Editor
    {
        private UVInspectorTool m_Tool;

        // Foldout states
        private static bool s_DisplayOptionsFoldout = false;
        private static bool s_QuickFixFoldout = true;

        private void OnEnable()
        {
            m_Tool = (UVInspectorTool)target;
            if (m_Tool != null)
            {
                m_Tool.CacheOriginalMesh();
            }
        }

        private void OnSceneGUI()
        {
            if (m_Tool == null) m_Tool = (UVInspectorTool)target;
            if (m_Tool == null || !m_Tool.showSceneHighlight) return;
            if (m_Tool.selectedVertices == null || m_Tool.selectedVertices.Count == 0) return;

            Mesh mesh = m_CurrentFrameMesh ?? m_Tool.GetCurrentSharedMesh();
            if (mesh == null) return;

            UVEditorCore.DrawSceneGUI(
                m_Tool.transform,
                mesh,
                m_Tool.selectedSubmesh,
                m_Tool.activeChannel,
                m_Tool.selectedVertices,
                m_Tool.selectionMode,
                m_Tool.showSceneHighlight,
                m_Tool.showSceneUVLabels,
                m_Tool.sceneHighlightColor,
                () => {
                    EditorUtility.SetDirty(m_Tool);
                    Repaint();
                    SceneView.RepaintAll();
                },
                m_Tool.selectConnectedFBX
            );
        }

        private Mesh m_CurrentFrameMesh;
        private Rect m_LastInspectorRect;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (m_Tool == null) m_Tool = (UVInspectorTool)target;

            if (Event.current.type == EventType.Layout)
            {
                m_CurrentFrameMesh = m_Tool.GetCurrentSharedMesh();
            }
            Mesh mesh = m_CurrentFrameMesh ?? m_Tool.GetCurrentSharedMesh();

            Event evt = Event.current;
            bool isHovered = m_LastInspectorRect.width > 0 && m_LastInspectorRect.Contains(evt.mousePosition);

            // Handle Undo/Redo only when mouse is hovering over the UV Preview & Fixer
            if (mesh != null && isHovered)
            {
                UVEditorCore.HandleUndoRedoEvents(evt, isHovered, mesh, m_Tool.activeChannel, m_Tool.selectedVertices, () =>
                {
                    EditorUtility.SetDirty(m_Tool);
                    Repaint();
                    SceneView.RepaintAll();
                });
            }

            Rect fullRect = EditorGUILayout.BeginVertical();

            // 1. Mesh Information Banner
            DrawMeshHeader(mesh);

            if (mesh == null)
            {
                EditorGUILayout.HelpBox("No Mesh found on this GameObject. Please ensure a MeshFilter or SkinnedMeshRenderer is present and has a valid Mesh assigned.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            // 2. Channel & Submesh Toolbar
            DrawChannelToolbar(mesh);

            // 2.2. Material & Texture Tabs
            DrawMaterialTextureTabs();

            // 2.5. Selection Toolbar (Select/Pan, Island/Face/Vertex, Select All/None/Invert)
            DrawSelectionToolbar(mesh);

            // 3. Interactive UV Preview Canvas
            DrawPreviewSection(mesh);

            EditorGUILayout.Space(6);

            // 4. Display & Visual Options Foldout
            DrawDisplaySettings();

            EditorGUILayout.Space(6);

            // 5. Quick Fix Options Foldout
            DrawQuickFixSection(mesh);

            EditorGUILayout.EndVertical();

            if (evt.type == EventType.Repaint)
            {
                m_LastInspectorRect = fullRect;
            }

            serializedObject.ApplyModifiedProperties();
        }

        #region Header & Info

        private void DrawMeshHeader(Mesh mesh)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
                    GUILayout.Label("UV Map Preview & Fixer", titleStyle);

                    GUILayout.FlexibleSpace();

                    if (mesh != null)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(mesh);
                        bool isAsset = !string.IsNullOrEmpty(assetPath);

                        if (isAsset)
                        {
                            GUI.color = new Color(0.6f, 0.9f, 1f);
                            GUILayout.Label(assetPath.EndsWith(".asset") ? "[Project Asset]" : "[Imported Model]", EditorStyles.miniBoldLabel);
                        }
                        else
                        {
                            GUI.color = new Color(1f, 0.85f, 0.4f);
                            GUILayout.Label("[Scene Instance]", EditorStyles.miniBoldLabel);
                        }
                        GUI.color = Color.white;
                    }
                }

                if (mesh != null)
                {
                    EditorGUILayout.LabelField("Mesh:", $"{mesh.name} ({mesh.vertexCount:N0} verts, {mesh.triangles.Length / 3:N0} tris, {mesh.subMeshCount} submesh)");
                }
            }
        }

        #endregion

        #region Channel Toolbar

        private void DrawChannelToolbar(Mesh mesh)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Channel:", EditorStyles.miniBoldLabel, GUILayout.Width(55));

                for (int ch = 0; ch < 4; ch++)
                {
                    bool hasUV = UVEditorCore.HasUVs(mesh, ch);
                    string label = hasUV ? $"UV{ch} •" : $"UV{ch}";
                    bool isSelected = m_Tool.activeChannel == ch;

                    GUI.backgroundColor = isSelected ? new Color(0.35f, 0.75f, 1f) : Color.white;
                    if (GUILayout.Toggle(isSelected, label, EditorStyles.toolbarButton, GUILayout.Width(54)) && !isSelected)
                    {
                        Undo.RecordObject(m_Tool, "Change UV Channel");
                        m_Tool.activeChannel = ch;
                    }
                    GUI.backgroundColor = Color.white;
                }

                GUILayout.FlexibleSpace();

                // Submesh filter popup
                if (mesh.subMeshCount > 1)
                {
                    string[] submeshNames = new string[mesh.subMeshCount + 1];
                    submeshNames[0] = "All Submeshes";
                    for (int i = 0; i < mesh.subMeshCount; i++) submeshNames[i + 1] = $"Submesh {i}";

                    int currentIdx = m_Tool.selectedSubmesh + 1;
                    int newIdx = EditorGUILayout.Popup(currentIdx, submeshNames, EditorStyles.toolbarPopup, GUILayout.Width(110));
                    if (newIdx != currentIdx)
                    {
                        Undo.RecordObject(m_Tool, "Change Submesh View");
                        m_Tool.selectedSubmesh = newIdx - 1;
                        if (newIdx > 0)
                        {
                            m_Tool.activeMaterialIndex = newIdx - 1;
                            m_Tool.activeTextureIndex = 0;
                            m_Tool.backgroundMode = UVBackgroundMode.MaterialTexture;
                            m_Tool.selectedVertices.Clear();
                        }
                        UVEditorCore.InvalidateCache();
                        EditorUtility.SetDirty(m_Tool);
                    }
                }
            }
        }

        #region Material & Texture Tabs

        private void DrawMaterialTextureTabs()
        {
            Material[] mats = m_Tool.GetTargetMaterials();
            Mesh mesh = m_CurrentFrameMesh ?? m_Tool.GetCurrentSharedMesh();
            int submeshCount = mesh != null ? mesh.subMeshCount : 0;
            int totalCount = Mathf.Max(mats != null ? mats.Length : 0, submeshCount);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (totalCount > 0)
                {
                    // Row 1: Material Tabs
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                    {
                        GUILayout.Label("Material:", EditorStyles.miniBoldLabel, GUILayout.Width(58));

                        bool hasMultiple = totalCount > 1;
                        int tabCount = hasMultiple ? (totalCount + 1) : 1;
                        string[] tabNames = new string[tabCount];

                        if (hasMultiple)
                        {
                            tabNames[0] = "All";
                            for (int i = 0; i < totalCount; i++)
                            {
                                string matName = (mats != null && i < mats.Length && mats[i] != null) ? mats[i].name : $"(Submesh {i})";
                                tabNames[i + 1] = $"{i}: {matName}";
                            }
                        }
                        else
                        {
                            string matName = (mats != null && mats.Length > 0 && mats[0] != null) ? mats[0].name : "Submesh 0";
                            tabNames[0] = $"0: {matName}";
                        }

                        int currentTab;
                        if (hasMultiple)
                        {
                            if (m_Tool.selectedSubmesh < 0)
                            {
                                currentTab = 0; // "All"
                            }
                            else
                            {
                                currentTab = Mathf.Clamp(m_Tool.selectedSubmesh + 1, 1, tabCount - 1);
                            }
                        }
                        else
                        {
                            currentTab = 0;
                        }

                        int newTab = GUILayout.Toolbar(currentTab, tabNames, EditorStyles.toolbarButton);
                        if (newTab != currentTab)
                        {
                            Undo.RecordObject(m_Tool, "Change Material Tab");
                            if (hasMultiple)
                            {
                                if (newTab == 0)
                                {
                                    m_Tool.selectedSubmesh = -1;
                                }
                                else
                                {
                                    int submeshIdx = newTab - 1;
                                    m_Tool.selectedSubmesh = submeshIdx;
                                    m_Tool.activeMaterialIndex = submeshIdx;
                                    m_Tool.activeTextureIndex = 0;
                                    m_Tool.backgroundMode = UVBackgroundMode.MaterialTexture;
                                    m_Tool.selectedVertices.Clear();
                                }
                            }
                            else
                            {
                                m_Tool.selectedSubmesh = 0;
                                m_Tool.activeMaterialIndex = 0;
                                m_Tool.activeTextureIndex = 0;
                                m_Tool.backgroundMode = UVBackgroundMode.MaterialTexture;
                            }
                            UVEditorCore.InvalidateCache();
                            EditorUtility.SetDirty(m_Tool);
                            Repaint();
                        }
                    }

                    // Row 2: Texture Tabs for the active material
                    var slots = m_Tool.GetActiveMaterialTextureSlots();
                    Material activeMat = m_Tool.GetActiveMaterial();

                    if (slots != null && slots.Count > 0)
                    {
                        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                        {
                            GUILayout.Label("Texture:", EditorStyles.miniBoldLabel, GUILayout.Width(58));

                            string[] texNames = new string[slots.Count];
                            for (int t = 0; t < slots.Count; t++)
                            {
                                texNames[t] = slots[t].displayName;
                            }

                            int currentTex = Mathf.Clamp(m_Tool.activeTextureIndex, 0, slots.Count - 1);
                            int newTex = GUILayout.Toolbar(currentTex, texNames, EditorStyles.toolbarButton);
                            if (newTex != currentTex)
                            {
                                Undo.RecordObject(m_Tool, "Change Texture Tab");
                                m_Tool.activeTextureIndex = newTex;
                                m_Tool.backgroundMode = UVBackgroundMode.MaterialTexture;
                                EditorUtility.SetDirty(m_Tool);
                                Repaint();
                            }
                        }

                        // Row 3: Active Texture summary line & Ping buttons
                        int activeIdx = Mathf.Clamp(m_Tool.activeTextureIndex, 0, slots.Count - 1);
                        var activeSlot = slots[activeIdx];
                        Texture2D tex = activeSlot.texture;

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            string submeshInfo = m_Tool.selectedSubmesh >= 0 ? $"Submesh {m_Tool.selectedSubmesh}" : "All Submeshes";
                            string matName = activeMat != null ? activeMat.name : "(None)";
                            string texInfo = tex != null
                                ? $"<b>{matName}</b> ({submeshInfo}) ↔ <b>{tex.name}</b> ({tex.width}x{tex.height}) • Slot: <code>{activeSlot.propertyName}</code>"
                                : $"<b>{matName}</b> ({submeshInfo}) • Slot: <code>{activeSlot.propertyName}</code> (No texture assigned)";

                            GUILayout.Label(texInfo, new GUIStyle(EditorStyles.miniLabel) { richText = true });
                            GUILayout.FlexibleSpace();

                            if (activeMat != null && GUILayout.Button("Ping Mat", EditorStyles.miniButton, GUILayout.Width(62)))
                            {
                                EditorGUIUtility.PingObject(activeMat);
                            }

                            if (tex != null && GUILayout.Button("Ping Tex", EditorStyles.miniButton, GUILayout.Width(62)))
                            {
                                EditorGUIUtility.PingObject(tex);
                            }
                        }
                    }
                    else
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            string matName = activeMat != null ? activeMat.name : "(None)";
                            string submeshInfo = m_Tool.selectedSubmesh >= 0 ? $"Submesh {m_Tool.selectedSubmesh}" : "All Submeshes";
                            GUILayout.Label($"<b>{matName}</b> ({submeshInfo}): No assigned Texture2D properties detected.", new GUIStyle(EditorStyles.miniLabel) { richText = true });
                            GUILayout.FlexibleSpace();
                            if (activeMat != null && GUILayout.Button("Ping Mat", EditorStyles.miniButton, GUILayout.Width(62)))
                            {
                                EditorGUIUtility.PingObject(activeMat);
                            }
                        }
                    }
                }
                else
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("<b>Material:</b> [No Material Assigned to Renderer]", new GUIStyle(EditorStyles.miniLabel) { richText = true });
                    }
                }
            }
        }

        #endregion

        private void DrawSelectionToolbar(Mesh mesh)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // Canvas Tool: Select / Move / Pan
                GUILayout.Label("Tool:", EditorStyles.miniBoldLabel, GUILayout.Width(35));
                bool isSelect = m_Tool.activeCanvasTool == CanvasTool.Select;
                GUI.backgroundColor = isSelect ? new Color(0.35f, 0.75f, 1f) : Color.white;
                if (GUILayout.Toggle(isSelect, "Select", EditorStyles.toolbarButton, GUILayout.Width(50)) && !isSelect)
                {
                    Undo.RecordObject(m_Tool, "Change Canvas Tool");
                    m_Tool.activeCanvasTool = CanvasTool.Select;
                }
                bool isMove = m_Tool.activeCanvasTool == CanvasTool.Move;
                GUI.backgroundColor = isMove ? new Color(0.35f, 0.75f, 1f) : Color.white;
                if (GUILayout.Toggle(isMove, "Move", EditorStyles.toolbarButton, GUILayout.Width(46)) && !isMove)
                {
                    Undo.RecordObject(m_Tool, "Change Canvas Tool");
                    m_Tool.activeCanvasTool = CanvasTool.Move;
                }
                bool isPan = m_Tool.activeCanvasTool == CanvasTool.Pan;
                GUI.backgroundColor = isPan ? new Color(0.35f, 0.75f, 1f) : Color.white;
                if (GUILayout.Toggle(isPan, "Pan", EditorStyles.toolbarButton, GUILayout.Width(40)) && !isPan)
                {
                    Undo.RecordObject(m_Tool, "Change Canvas Tool");
                    m_Tool.activeCanvasTool = CanvasTool.Pan;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.Space(6);

                // Selection Mode: Island / Quad / Face / Vertex
                GUILayout.Label("Mode:", EditorStyles.miniBoldLabel, GUILayout.Width(38));

                bool isIsland = m_Tool.selectionMode == UVSelectionMode.Island;
                GUI.backgroundColor = isIsland ? new Color(1f, 0.85f, 0.35f) : Color.white;
                if (GUILayout.Toggle(isIsland, new GUIContent("Island", "Select connected UV Island (Hotkey: L)"), EditorStyles.toolbarButton, GUILayout.Width(50)) && !isIsland)
                {
                    Undo.RecordObject(m_Tool, "Change Selection Mode");
                    m_Tool.selectionMode = UVSelectionMode.Island;
                }

                bool isQuad = m_Tool.selectionMode == UVSelectionMode.Quad;
                GUI.backgroundColor = isQuad ? new Color(1f, 0.85f, 0.35f) : Color.white;
                if (GUILayout.Toggle(isQuad, new GUIContent("Quad", "Select whole connected Quad (2 triangles)"), EditorStyles.toolbarButton, GUILayout.Width(46)) && !isQuad)
                {
                    Undo.RecordObject(m_Tool, "Change Selection Mode");
                    m_Tool.selectionMode = UVSelectionMode.Quad;
                }

                bool isFace = m_Tool.selectionMode == UVSelectionMode.Face;
                GUI.backgroundColor = isFace ? new Color(1f, 0.85f, 0.35f) : Color.white;
                if (GUILayout.Toggle(isFace, new GUIContent("Face", "Select Face / Triangle (Alt+Click for single tri)"), EditorStyles.toolbarButton, GUILayout.Width(42)) && !isFace)
                {
                    Undo.RecordObject(m_Tool, "Change Selection Mode");
                    m_Tool.selectionMode = UVSelectionMode.Face;
                }

                bool isVertex = m_Tool.selectionMode == UVSelectionMode.Vertex;
                GUI.backgroundColor = isVertex ? new Color(1f, 0.85f, 0.35f) : Color.white;
                if (GUILayout.Toggle(isVertex, new GUIContent("Vertex", "Select individual UV Vertex"), EditorStyles.toolbarButton, GUILayout.Width(48)) && !isVertex)
                {
                    Undo.RecordObject(m_Tool, "Change Selection Mode");
                    m_Tool.selectionMode = UVSelectionMode.Vertex;
                }
                GUI.backgroundColor = Color.white;

                bool isConn = m_Tool.selectConnectedFBX;
                GUI.backgroundColor = isConn ? new Color(0.4f, 0.85f, 0.45f) : Color.white;
                if (GUILayout.Toggle(isConn, new GUIContent("🔗 FBX", "Select connected vertices also (like in original FBX / 3D model)"), EditorStyles.toolbarButton, GUILayout.Width(50)) != isConn)
                {
                    Undo.RecordObject(m_Tool, "Toggle Connect FBX Vertices");
                    m_Tool.selectConnectedFBX = !isConn;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.Space(6);

                var overlapInfo = UVEditorCore.GetIslandOverlapInfo(mesh, m_Tool.activeChannel, m_Tool.selectedSubmesh);
                if (overlapInfo != null)
                {
                    if (overlapInfo.totalOverlappingIslands > 0)
                    {
                        GUILayout.Label($"<color=#ff9933><b>🗂 {overlapInfo.totalOverlappingIslands} Overlaps ({overlapInfo.sharedGroups.Count} Shared)</b></color>", new GUIStyle(EditorStyles.miniBoldLabel) { richText = true });
                    }
                    else
                    {
                        GUILayout.Label("<color=#88cc88>✓ 0 Overlaps</color>", new GUIStyle(EditorStyles.miniLabel) { richText = true });
                    }
                }

                GUILayout.FlexibleSpace();

                // Selection Count badge & quick actions
                int selCount = m_Tool.selectedVertices.Count;
                if (selCount > 0)
                {
                    GUILayout.Label($"<color=#ffd24d>★ {selCount:N0} verts</color>", new GUIStyle(EditorStyles.miniBoldLabel) { richText = true });
                }

                if (GUILayout.Button("All", EditorStyles.toolbarButton, GUILayout.Width(32)))
                {
                    m_Tool.SelectAll(mesh, m_Tool.activeChannel);
                    UVEditorCore.InvalidateCache();
                    Repaint();
                }

                if (GUILayout.Button("None", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    m_Tool.ClearSelection();
                    UVEditorCore.InvalidateCache();
                    Repaint();
                }

                if (GUILayout.Button("Invert", EditorStyles.toolbarButton, GUILayout.Width(44)))
                {
                    m_Tool.InvertSelection(mesh);
                    UVEditorCore.InvalidateCache();
                    Repaint();
                }
            }
        }

        #endregion

        #region Preview Section

        private void DrawPreviewSection(Mesh mesh)
        {
            Vector2[] liveUVs = null;

            // Reserve Rect for Canvas
            float height = m_Tool.previewHeight;
            Rect canvasRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(height), GUILayout.ExpandWidth(true));

            // Fetch materials & textures
            Texture2D matTex = m_Tool.GetMaterialTexture();

            // Draw the interactive canvas
            Vector2 center = m_Tool.viewCenter;
            float zoom = m_Tool.viewZoom;

            UVEditorCore.DrawPreviewGUI(
                canvasRect,
                mesh,
                m_Tool.activeChannel,
                m_Tool.selectedSubmesh,
                m_Tool.backgroundMode,
                m_Tool.customTexture,
                matTex,
                m_Tool.textureAlpha,
                m_Tool.repeatTexture,
                m_Tool.wireColor,
                m_Tool.faceFillColor,
                m_Tool.flippedFaceColor,
                m_Tool.showWireframe,
                m_Tool.showFaces,
                m_Tool.showVertices,
                m_Tool.showFlippedFaces,
                m_Tool.showGrid,
                m_Tool.showUnitBounds,
                ref center,
                ref zoom,
                liveUVs,
                m_Tool.selectedVertices,
                m_Tool.selectionMode,
                m_Tool.activeCanvasTool,
                () => {
                    EditorUtility.SetDirty(m_Tool);
                    Repaint();
                    SceneView.RepaintAll();
                },
                newUVs => ApplyUVFix(mesh, _ => newUVs, "Move Selected UVs"),
                () => Repaint(),
                m_Tool.showOverlapIndicators,
                m_Tool.overlapHighlightColor,
                m_Tool.selectConnectedFBX
            );

            if (center != m_Tool.viewCenter || !Mathf.Approximately(zoom, m_Tool.viewZoom))
            {
                m_Tool.viewCenter = center;
                m_Tool.viewZoom = zoom;
                EditorUtility.SetDirty(m_Tool);
            }

            // Controls under canvas: Reset, Frame, Stats
            Vector2[] displayUVs = liveUVs ?? UVEditorCore.GetUVs(mesh, m_Tool.activeChannel);
            UVEditorCore.CalculateUVBounds(displayUVs, out Vector2 minUV, out Vector2 maxUV);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool canUndo = UVEditorCore.UVHistory.CanUndo(mesh, m_Tool.activeChannel);
                bool canRedo = UVEditorCore.UVHistory.CanRedo(mesh, m_Tool.activeChannel);
                string undoName = UVEditorCore.UVHistory.GetUndoName(mesh, m_Tool.activeChannel);
                string redoName = UVEditorCore.UVHistory.GetRedoName(mesh, m_Tool.activeChannel);

                string undoTooltip = canUndo ? $"Undo: {undoName} (Ctrl+Z while hovered)" : "Undo (Ctrl+Z while hovered)";
                string redoTooltip = canRedo ? $"Redo: {redoName} (Ctrl+Y while hovered)" : "Redo (Ctrl+Y while hovered)";

                using (new EditorGUI.DisabledScope(!canUndo))
                {
                    GUI.backgroundColor = canUndo ? new Color(0.85f, 0.95f, 1f) : Color.white;
                    if (GUILayout.Button(new GUIContent("↶ Undo", undoTooltip), EditorStyles.toolbarButton, GUILayout.Width(58)))
                    {
                        PerformUndo(mesh);
                    }
                    GUI.backgroundColor = Color.white;
                }

                using (new EditorGUI.DisabledScope(!canRedo))
                {
                    GUI.backgroundColor = canRedo ? new Color(0.85f, 0.95f, 1f) : Color.white;
                    if (GUILayout.Button(new GUIContent("↷ Redo", redoTooltip), EditorStyles.toolbarButton, GUILayout.Width(58)))
                    {
                        PerformRedo(mesh);
                    }
                    GUI.backgroundColor = Color.white;
                }

                GUILayout.Space(6);

                if (GUILayout.Button("Reset View [0..1]", EditorStyles.toolbarButton, GUILayout.Width(105)))
                {
                    m_Tool.ResetView();
                }

                if (GUILayout.Button("Focus Bounds", EditorStyles.toolbarButton, GUILayout.Width(88)))
                {
                    FocusUVBounds(minUV, maxUV);
                }

                GUILayout.FlexibleSpace();

                // Live Bounds display
                GUILayout.Label($"Bounds: [{minUV.x:F2}, {minUV.y:F2}]..[{maxUV.x:F2}, {maxUV.y:F2}]", EditorStyles.miniLabel);
            }

            // Warnings bar if UVs are outside 0..1
            bool outOfBounds = minUV.x < -0.001f || minUV.y < -0.001f || maxUV.x > 1.001f || maxUV.y > 1.001f;
            if (outOfBounds)
            {
                EditorGUILayout.HelpBox("⚠ UVs extend beyond [0, 1] bounds.", MessageType.Warning);
            }
        }

        private void FocusUVBounds(Vector2 min, Vector2 max)
        {
            Vector2 center = (min + max) * 0.5f;
            float w = Mathf.Max(0.01f, max.x - min.x);
            float h = Mathf.Max(0.01f, max.y - min.y);
            float maxDim = Mathf.Max(w, h);

            m_Tool.viewCenter = center;
            m_Tool.viewZoom = Mathf.Clamp(1.1f / maxDim, 0.1f, 50f);
        }

        #endregion

        #region Display Settings

        private void DrawDisplaySettings()
        {
            s_DisplayOptionsFoldout = EditorGUILayout.Foldout(s_DisplayOptionsFoldout, "Display & Visual Options", true, EditorStyles.foldoutHeader);
            if (!s_DisplayOptionsFoldout) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Background & Texture
                m_Tool.backgroundMode = (UVBackgroundMode)EditorGUILayout.EnumPopup("Background Mode", m_Tool.backgroundMode);

                if (m_Tool.backgroundMode == UVBackgroundMode.CustomTexture)
                {
                    m_Tool.customTexture = (Texture2D)EditorGUILayout.ObjectField("Custom Texture", m_Tool.customTexture, typeof(Texture2D), false);
                }

                if (m_Tool.backgroundMode == UVBackgroundMode.MaterialTexture || m_Tool.backgroundMode == UVBackgroundMode.CustomTexture)
                {
                    m_Tool.textureAlpha = EditorGUILayout.Slider("Texture Opacity", m_Tool.textureAlpha, 0f, 1f);
                    m_Tool.repeatTexture = EditorGUILayout.Toggle("Repeat / Tile Texture", m_Tool.repeatTexture);
                }

                EditorGUILayout.Space(4);

                // Overlays
                using (new EditorGUILayout.HorizontalScope())
                {
                    m_Tool.showWireframe = EditorGUILayout.ToggleLeft("Wireframe", m_Tool.showWireframe, GUILayout.Width(90));
                    m_Tool.showFaces = EditorGUILayout.ToggleLeft("Face Fill", m_Tool.showFaces, GUILayout.Width(80));
                    m_Tool.showFlippedFaces = EditorGUILayout.ToggleLeft("Flipped Faces", m_Tool.showFlippedFaces, GUILayout.Width(100));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    m_Tool.showVertices = EditorGUILayout.ToggleLeft("Vertices", m_Tool.showVertices, GUILayout.Width(90));
                    m_Tool.showGrid = EditorGUILayout.ToggleLeft("Grid", m_Tool.showGrid, GUILayout.Width(80));
                    m_Tool.showUnitBounds = EditorGUILayout.ToggleLeft("[0,1] Bounds", m_Tool.showUnitBounds, GUILayout.Width(100));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    m_Tool.showOverlapIndicators = EditorGUILayout.ToggleLeft("Overlap Indicators", m_Tool.showOverlapIndicators, GUILayout.Width(130));
                }

                EditorGUILayout.Space(4);

                // Colors
                m_Tool.wireColor = EditorGUILayout.ColorField("Wireframe Color", m_Tool.wireColor);
                if (m_Tool.showFaces)
                {
                    m_Tool.faceFillColor = EditorGUILayout.ColorField("Face Fill Color", m_Tool.faceFillColor);
                }
                if (m_Tool.showFlippedFaces)
                {
                    m_Tool.flippedFaceColor = EditorGUILayout.ColorField("Flipped Highlight Color", m_Tool.flippedFaceColor);
                }
                if (m_Tool.showOverlapIndicators)
                {
                    m_Tool.overlapHighlightColor = EditorGUILayout.ColorField("Overlap Highlight Color", m_Tool.overlapHighlightColor);
                }

                m_Tool.previewHeight = EditorGUILayout.Slider("Canvas Height", m_Tool.previewHeight, 200f, 550f);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("3D Scene View Highlight", EditorStyles.boldLabel);
                m_Tool.showSceneHighlight = EditorGUILayout.Toggle("Show in Scene View", m_Tool.showSceneHighlight);
                if (m_Tool.showSceneHighlight)
                {
                    EditorGUI.indentLevel++;
                    m_Tool.showSceneUVLabels = EditorGUILayout.Toggle("Show UV Labels in Scene", m_Tool.showSceneUVLabels);
                    m_Tool.sceneHighlightColor = EditorGUILayout.ColorField("Scene Highlight Color", m_Tool.sceneHighlightColor);
                    EditorGUI.indentLevel--;
                }
            }
        }

        #endregion

        #region Quick Fix Options

        private void DrawQuickFixSection(Mesh mesh)
        {
            s_QuickFixFoldout = EditorGUILayout.Foldout(s_QuickFixFoldout, "Quick Fix Options", true, EditorStyles.foldoutHeader);
            if (!s_QuickFixFoldout) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int selCount = m_Tool.selectedVertices.Count;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("1-Click Common UV Fixes", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (selCount > 0)
                    {
                        GUILayout.Label($"<color=#ffd24d>Target: {selCount:N0} selected verts</color>", new GUIStyle(EditorStyles.miniBoldLabel) { richText = true });
                    }
                    else
                    {
                        GUILayout.Label("Target: All vertices", EditorStyles.miniLabel);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool canUndo = UVEditorCore.UVHistory.CanUndo(mesh, m_Tool.activeChannel);
                    bool canRedo = UVEditorCore.UVHistory.CanRedo(mesh, m_Tool.activeChannel);
                    string undoName = UVEditorCore.UVHistory.GetUndoName(mesh, m_Tool.activeChannel);
                    string redoName = UVEditorCore.UVHistory.GetRedoName(mesh, m_Tool.activeChannel);

                    string undoTooltip = canUndo ? $"Undo: {undoName} (Ctrl+Z while hovered)" : "Undo (Ctrl+Z while hovered)";
                    string redoTooltip = canRedo ? $"Redo: {redoName} (Ctrl+Y while hovered)" : "Redo (Ctrl+Y while hovered)";

                    using (new EditorGUI.DisabledScope(!canUndo))
                    {
                        GUI.backgroundColor = canUndo ? new Color(0.85f, 0.95f, 1f) : Color.white;
                        string uLabel = canUndo ? $"↶ Undo ({undoName})" : "↶ Undo";
                        if (GUILayout.Button(new GUIContent(uLabel, undoTooltip), GUILayout.Height(24)))
                        {
                            PerformUndo(mesh);
                        }
                        GUI.backgroundColor = Color.white;
                    }

                    using (new EditorGUI.DisabledScope(!canRedo))
                    {
                        GUI.backgroundColor = canRedo ? new Color(0.85f, 0.95f, 1f) : Color.white;
                        string rLabel = canRedo ? $"↷ Redo ({redoName})" : "↷ Redo";
                        if (GUILayout.Button(new GUIContent(rLabel, redoTooltip), GUILayout.Height(24)))
                        {
                            PerformRedo(mesh);
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }

                EditorGUILayout.Space(2);

                // Section: Flip Selected UV Origin (Flips around center/origin of selected UV)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.backgroundColor = new Color(0.28f, 0.72f, 1.0f); // Bright Cyan
                    string flipLabel = selCount > 0 ? "⇕ Flip Selected UV Origin" : "⇕ Flip UV Origin (In-Place)";
                    string flipTooltip = selCount > 0
                        ? "Flips the selected UV in-place around the center/origin of the selection"
                        : "Flips the entire mesh UVs in-place around their bounding box center";

                    if (GUILayout.Button(new GUIContent(flipLabel, flipTooltip), GUILayout.Height(28)))
                    {
                        int[] tris = UVEditorCore.GetTriangles(mesh, m_Tool.selectedSubmesh);
                        ApplyUVFix(mesh, uvs => UVEditorCore.FlipSelectedUVOrigin(uvs, m_Tool.flipOriginAxis, m_Tool.selectedVertices, tris), "Flip Selected UV Origin");
                    }
                    GUI.backgroundColor = Color.white;

                    m_Tool.flipOriginAxis = (UVFlipAxis)EditorGUILayout.EnumPopup(m_Tool.flipOriginAxis, GUILayout.Width(82), GUILayout.Height(28));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("↕ Flip V (Selected Origin)", "Flips vertically around the center of the selected UV in-place"), GUILayout.Height(22)))
                    {
                        int[] tris = UVEditorCore.GetTriangles(mesh, m_Tool.selectedSubmesh);
                        ApplyUVFix(mesh, uvs => UVEditorCore.FlipSelectedUVOrigin(uvs, UVFlipAxis.Vertical, m_Tool.selectedVertices, tris), "Flip Selected UV Origin V");
                    }
                    if (GUILayout.Button(new GUIContent("↔ Flip U (Selected Origin)", "Flips horizontally around the center of the selected UV in-place"), GUILayout.Height(22)))
                    {
                        int[] tris = UVEditorCore.GetTriangles(mesh, m_Tool.selectedSubmesh);
                        ApplyUVFix(mesh, uvs => UVEditorCore.FlipSelectedUVOrigin(uvs, UVFlipAxis.Horizontal, m_Tool.selectedVertices, tris), "Flip Selected UV Origin U");
                    }
                }

                EditorGUILayout.Space(2);

                // Section: Canvas Center Flips (around canvas 0.5 center)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
                    if (GUILayout.Button(new GUIContent("↕ Flip V (Canvas Center)", "Flips vertically across canvas center [0.5] (Fix inverted Y imports)"), GUILayout.Height(24)))
                    {
                        ApplyUVFix(mesh, uvs => UVEditorCore.FlipV(uvs, true, m_Tool.selectedVertices), "Flip V (Canvas Center)");
                    }
                    GUI.backgroundColor = Color.white;

                    if (GUILayout.Button(new GUIContent("↔ Flip U (Canvas Center)", "Flips horizontally across canvas center [0.5] (Mirror X)"), GUILayout.Height(24)))
                    {
                        ApplyUVFix(mesh, uvs => UVEditorCore.FlipU(uvs, true, m_Tool.selectedVertices), "Flip U (Canvas Center)");
                    }
                }

                EditorGUILayout.Space(4);

                // Row 2: Rotations
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("↻ Rotate 90° CW"))
                    {
                        Vector2[] uvs = UVEditorCore.GetUVs(mesh, m_Tool.activeChannel);
                        Vector2 pivot = GetPivotCoordinates(uvs, m_Tool.selectedVertices);
                        ApplyUVFix(mesh, u => UVEditorCore.Rotate90(u, true, pivot, m_Tool.selectedVertices), "Rotate 90 CW");
                    }
                    if (GUILayout.Button("↺ Rotate 90° CCW"))
                    {
                        Vector2[] uvs = UVEditorCore.GetUVs(mesh, m_Tool.activeChannel);
                        Vector2 pivot = GetPivotCoordinates(uvs, m_Tool.selectedVertices);
                        ApplyUVFix(mesh, u => UVEditorCore.Rotate90(u, false, pivot, m_Tool.selectedVertices), "Rotate 90 CCW");
                    }
                    if (GUILayout.Button("Rotate 180°"))
                    {
                        Vector2[] uvs = UVEditorCore.GetUVs(mesh, m_Tool.activeChannel);
                        Vector2 pivot = GetPivotCoordinates(uvs, m_Tool.selectedVertices);
                        ApplyUVFix(mesh, u => UVEditorCore.Rotate(u, 180f, pivot, m_Tool.selectedVertices), "Rotate 180");
                    }
                }

                EditorGUILayout.Space(4);

                // Row 3: Fit & Normalize
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Fit to [0, 1] (Keep Aspect)"))
                    {
                        ApplyUVFix(mesh, u => UVEditorCore.FitPreserveAspect(u, m_Tool.selectedVertices), "Fit UV Aspect");
                    }
                    if (GUILayout.Button("Normalize to [0, 1] (Stretch)"))
                    {
                        ApplyUVFix(mesh, u => UVEditorCore.NormalizeUVs(u, m_Tool.selectedVertices), "Normalize UVs");
                    }
                    if (GUILayout.Button("Center (to 0.5, 0.5)"))
                    {
                        ApplyUVFix(mesh, u => UVEditorCore.CenterUVs(u, m_Tool.selectedVertices), "Center UVs");
                    }
                }

                EditorGUILayout.Space(4);

                // Row 4: Clamp & Wrap
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Clamp [0, 1]"))
                    {
                        ApplyUVFix(mesh, u => UVEditorCore.Clamp01(u, m_Tool.selectedVertices), "Clamp UVs");
                    }
                    if (GUILayout.Button("Wrap [0, 1] (Modulo)"))
                    {
                        ApplyUVFix(mesh, u => UVEditorCore.Wrap01(u, m_Tool.selectedVertices), "Wrap UVs");
                    }
                }
            }
        }

        #endregion

        private Vector2 GetPivotCoordinates(Vector2[] uvs, HashSet<int> selectedIndices = null)
        {
            if (selectedIndices != null && selectedIndices.Count > 0)
            {
                return GetCentroid(uvs, selectedIndices);
            }
            return new Vector2(0.5f, 0.5f);
        }

        private Vector2 GetCentroid(Vector2[] uvs, HashSet<int> selectedIndices = null)
        {
            if (uvs == null || uvs.Length == 0) return new Vector2(0.5f, 0.5f);
            UVEditorCore.CalculateUVBounds(uvs, selectedIndices, out Vector2 min, out Vector2 max);
            return (min + max) * 0.5f;
        }

        #region Helpers

        private Mesh EnsureInstance(Mesh mesh)
        {
            Mesh inst = UVEditorCore.EnsureMeshInstance(m_Tool, mesh, newMesh =>
            {
                m_Tool.SetCurrentSharedMesh(newMesh);
            });
            return inst;
        }

        private void ApplyUVFix(Mesh mesh, Func<Vector2[], Vector2[]> operation, string undoName)
        {
            Mesh instMesh = EnsureInstance(mesh);
            int channel = m_Tool.activeChannel;

            Vector2[] currentUVs = UVEditorCore.GetUVs(instMesh, channel);
            UVEditorCore.UVHistory.EnsureInitialState(instMesh, channel, currentUVs, "Initial State", m_Tool.selectedVertices);

            Vector2[] fixedUVs = operation(currentUVs);

            UVEditorCore.SetUVs(instMesh, channel, fixedUVs);
            EditorUtility.SetDirty(instMesh);
            EditorUtility.SetDirty(m_Tool);

            UVEditorCore.UVHistory.RecordStep(instMesh, channel, fixedUVs, undoName, m_Tool.selectedVertices);
        }

        private void PerformUndo(Mesh mesh)
        {
            Mesh instMesh = EnsureInstance(mesh);
            if (instMesh == null) return;
            if (UVEditorCore.UVHistory.PerformUndo(instMesh, m_Tool.activeChannel, m_Tool.selectedVertices, () =>
            {
                EditorUtility.SetDirty(instMesh);
                EditorUtility.SetDirty(m_Tool);
                Repaint();
                SceneView.RepaintAll();
            }))
            {
                Repaint();
                SceneView.RepaintAll();
            }
        }

        private void PerformRedo(Mesh mesh)
        {
            Mesh instMesh = EnsureInstance(mesh);
            if (instMesh == null) return;
            if (UVEditorCore.UVHistory.PerformRedo(instMesh, m_Tool.activeChannel, m_Tool.selectedVertices, () =>
            {
                EditorUtility.SetDirty(instMesh);
                EditorUtility.SetDirty(m_Tool);
                Repaint();
                SceneView.RepaintAll();
            }))
            {
                Repaint();
                SceneView.RepaintAll();
            }
        }

        #endregion

        #region Context Menus

        [MenuItem("CONTEXT/MeshFilter/Preview & Fix UVs...")]
        private static void AddToMeshFilter(MenuCommand command)
        {
            if (command.context is MeshFilter mf)
            {
                EnsureComponentOnGameObject(mf.gameObject);
            }
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/Preview & Fix UVs...")]
        private static void AddToSkinnedMeshRenderer(MenuCommand command)
        {
            if (command.context is SkinnedMeshRenderer smr)
            {
                EnsureComponentOnGameObject(smr.gameObject);
            }
        }

        [MenuItem("GameObject/Mesh/Add UV Preview & Fixer", false, 20)]
        private static void AddFromGameObjectMenu()
        {
            if (Selection.activeGameObject != null)
            {
                EnsureComponentOnGameObject(Selection.activeGameObject);
            }
        }

        private static void EnsureComponentOnGameObject(GameObject go)
        {
            if (!go.TryGetComponent<UVInspectorTool>(out var tool))
            {
                tool = Undo.AddComponent<UVInspectorTool>(go);
            }
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(tool);
        }

        #endregion
    }
}
#endif

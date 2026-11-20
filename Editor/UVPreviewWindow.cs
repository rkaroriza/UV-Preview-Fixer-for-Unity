#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UVTool.Editor
{
    public class UVPreviewWindow : EditorWindow
    {
        [MenuItem("Tools/UV Map Preview & Fixer", false, 100)]
        [MenuItem("Window/UV Map Preview & Fixer", false, 200)]
        public static void OpenWindow()
        {
            UVPreviewWindow window = GetWindow<UVPreviewWindow>("UV Preview & Fixer");
            window.minSize = new Vector2(380, 500);
            window.Show();
        }

        // Targets
        [SerializeField] private GameObject m_TargetGameObject;
        [SerializeField] private Mesh m_DirectMesh;
        [SerializeField] private bool m_AutoSyncSelection = true;

        // View & Preview state
        [SerializeField] private int m_ActiveChannel = 0;
        [SerializeField] private int m_SelectedSubmesh = -1;
        [SerializeField] private UVBackgroundMode m_BackgroundMode = UVBackgroundMode.MaterialTexture;
        [SerializeField] private Texture2D m_CustomTexture;
        [SerializeField] private float m_TextureAlpha = 0.85f;
        [SerializeField] private bool m_RepeatTexture = false;

        [SerializeField] private Color m_WireColor = new Color(0.2f, 0.9f, 1f, 0.85f);
        [SerializeField] private Color m_FaceFillColor = new Color(0.2f, 0.6f, 1f, 0f); // Default transparent (no inside color)
        [SerializeField] private Color m_FlippedFaceColor = new Color(0.2f, 0.6f, 1f, 0.15f);

        [SerializeField] private bool m_ShowWireframe = true;
        [SerializeField] private bool m_ShowFaces = false;
        [SerializeField] private bool m_ShowVertices = false;
        [SerializeField] private bool m_ShowFlippedFaces = false;
        [SerializeField] private bool m_ShowGrid = true;
        [SerializeField] private bool m_ShowUnitBounds = true;
        [SerializeField] private bool m_ShowOverlapIndicators = true;
        [SerializeField] private Color m_OverlapHighlightColor = new Color(1.0f, 0.55f, 0.15f, 0.95f);

        [SerializeField] private Vector2 m_ViewCenter = new Vector2(0.5f, 0.5f);
        [SerializeField] private float m_ViewZoom = 1.0f;

        [Header("Scene View 3D Highlight")]
        [SerializeField] private bool m_ShowSceneHighlight = true;
        [SerializeField] private bool m_ShowSceneUVLabels = true;
        [SerializeField] private Color m_SceneHighlightColor = new Color(1.0f, 0.75f, 0.18f, 0.45f);

        [Header("Selection")]
        [SerializeField] private UVSelectionMode m_SelectionMode = UVSelectionMode.Island;
        [SerializeField] private CanvasTool m_ActiveCanvasTool = CanvasTool.Select;
        [SerializeField] private UVFlipAxis m_FlipOriginAxis = UVFlipAxis.Vertical;
        [SerializeField] private bool m_SelectConnectedFBX = true;
        private readonly HashSet<int> m_SelectedVertices = new HashSet<int>();

        [Header("Material & Texture Tabs")]
        [SerializeField] private int m_ActiveMaterialIndex = 0;
        [SerializeField] private int m_ActiveTextureIndex = 0;

        // Foldouts & Scrolling
        private bool m_DisplayOptionsFoldout = false;
        private bool m_QuickFixFoldout = true;
        private Vector2 m_ScrollPosition;

        private void OnEnable()
        {
            wantsMouseMove = true;
            if (m_FlippedFaceColor.r > 0.7f && m_FlippedFaceColor.g < 0.4f)
            {
                m_ShowFlippedFaces = false;
                m_FlippedFaceColor = new Color(0.2f, 0.6f, 1f, 0.15f);
            }
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += OnSceneGUI;
            SyncSelection();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!m_ShowSceneHighlight) return;
            if (m_SelectedVertices == null || m_SelectedVertices.Count == 0) return;
            if (m_TargetGameObject == null) return;

            Mesh mesh = GetCurrentTargetMesh();
            if (mesh == null) return;

            UVEditorCore.DrawSceneGUI(
                m_TargetGameObject.transform,
                mesh,
                m_SelectedSubmesh,
                m_ActiveChannel,
                m_SelectedVertices,
                m_SelectionMode,
                m_ShowSceneHighlight,
                m_ShowSceneUVLabels,
                m_SceneHighlightColor,
                () => {
                    Repaint();
                    SceneView.RepaintAll();
                },
                m_SelectConnectedFBX
            );
        }

        private void OnSelectionChanged()
        {
            if (m_AutoSyncSelection)
            {
                SyncSelection();
                Repaint();
            }
        }

        private void SyncSelection()
        {
            if (Selection.activeGameObject != null)
            {
                m_TargetGameObject = Selection.activeGameObject;
                m_DirectMesh = null;
            }
            else if (Selection.activeObject is Mesh directMesh)
            {
                m_DirectMesh = directMesh;
                m_TargetGameObject = null;
            }
        }

        private Mesh GetCurrentTargetMesh()
        {
            if (m_DirectMesh != null) return m_DirectMesh;

            if (m_TargetGameObject != null)
            {
                if (m_TargetGameObject.TryGetComponent<MeshFilter>(out var mf))
                {
                    return mf.sharedMesh;
                }
                if (m_TargetGameObject.TryGetComponent<SkinnedMeshRenderer>(out var smr))
                {
                    return smr.sharedMesh;
                }
            }

            return null;
        }

        private void SetCurrentTargetMesh(Mesh mesh)
        {
            if (m_DirectMesh != null)
            {
                m_DirectMesh = mesh;
                return;
            }

            if (m_TargetGameObject != null)
            {
                if (m_TargetGameObject.TryGetComponent<MeshFilter>(out var mf))
                {
                    mf.sharedMesh = mesh;
                }
                else if (m_TargetGameObject.TryGetComponent<SkinnedMeshRenderer>(out var smr))
                {
                    smr.sharedMesh = mesh;
                }
            }
        }

        public Material[] GetTargetMaterials()
        {
            if (m_TargetGameObject != null && m_TargetGameObject.TryGetComponent<Renderer>(out var rend))
            {
                return rend.sharedMaterials;
            }
            if (m_DirectMesh != null)
            {
                string path = AssetDatabase.GetAssetPath(m_DirectMesh);
                if (!string.IsNullOrEmpty(path))
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                    List<Material> matList = new List<Material>();
                    foreach (var a in assets)
                    {
                        if (a is Material m && m != null) matList.Add(m);
                    }
                    if (matList.Count > 0) return matList.ToArray();
                }
            }
            return System.Array.Empty<Material>();
        }

        public Material GetActiveMaterial()
        {
            Material[] mats = GetTargetMaterials();
            if (mats == null || mats.Length == 0) return null;
            int idx = Mathf.Clamp(m_ActiveMaterialIndex, 0, mats.Length - 1);
            return mats[idx];
        }

        public List<MaterialTextureSlot> GetActiveMaterialTextureSlots()
        {
            Material mat = GetActiveMaterial();
            return UVInspectorTool.GetMaterialTextureSlots(mat);
        }

        private Texture2D GetTargetMaterialTexture()
        {
            var slots = GetActiveMaterialTextureSlots();
            if (slots == null || slots.Count == 0)
            {
                // Fallback check
                Material mat = GetActiveMaterial();
                if (mat != null)
                {
                    if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") is Texture2D baseMap) return baseMap;
                    if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") is Texture2D mainTex) return mainTex;
                }
                return null;
            }
            int idx = Mathf.Clamp(m_ActiveTextureIndex, 0, slots.Count - 1);
            return slots[idx].texture;
        }

        private void DrawMaterialTextureTabs()
        {
            Material[] mats = GetTargetMaterials();
            Mesh mesh = m_CurrentFrameMesh ?? GetCurrentTargetMesh();
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
                            if (m_SelectedSubmesh < 0)
                            {
                                currentTab = 0; // "All"
                            }
                            else
                            {
                                currentTab = Mathf.Clamp(m_SelectedSubmesh + 1, 1, tabCount - 1);
                            }
                        }
                        else
                        {
                            currentTab = 0;
                        }

                        int newTab = GUILayout.Toolbar(currentTab, tabNames, EditorStyles.toolbarButton);
                        if (newTab != currentTab)
                        {
                            if (hasMultiple)
                            {
                                if (newTab == 0)
                                {
                                    m_SelectedSubmesh = -1;
                                }
                                else
                                {
                                    int submeshIdx = newTab - 1;
                                    m_SelectedSubmesh = submeshIdx;
                                    m_ActiveMaterialIndex = submeshIdx;
                                    m_ActiveTextureIndex = 0;
                                    m_BackgroundMode = UVBackgroundMode.MaterialTexture;
                                    m_SelectedVertices.Clear();
                                }
                            }
                            else
                            {
                                m_SelectedSubmesh = 0;
                                m_ActiveMaterialIndex = 0;
                                m_ActiveTextureIndex = 0;
                                m_BackgroundMode = UVBackgroundMode.MaterialTexture;
                            }
                            UVEditorCore.InvalidateCache();
                            Repaint();
                        }
                    }

                    // Row 2: Texture Tabs for the active material
                    var slots = GetActiveMaterialTextureSlots();
                    Material activeMat = GetActiveMaterial();

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

                            int currentTex = Mathf.Clamp(m_ActiveTextureIndex, 0, slots.Count - 1);
                            int newTex = GUILayout.Toolbar(currentTex, texNames, EditorStyles.toolbarButton);
                            if (newTex != currentTex)
                            {
                                m_ActiveTextureIndex = newTex;
                                m_BackgroundMode = UVBackgroundMode.MaterialTexture;
                                Repaint();
                            }
                        }

                        // Row 3: Active Texture summary line & Ping buttons
                        int activeIdx = Mathf.Clamp(m_ActiveTextureIndex, 0, slots.Count - 1);
                        var activeSlot = slots[activeIdx];
                        Texture2D tex = activeSlot.texture;

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            string submeshInfo = m_SelectedSubmesh >= 0 ? $"Submesh {m_SelectedSubmesh}" : "All Submeshes";
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
                            string submeshInfo = m_SelectedSubmesh >= 0 ? $"Submesh {m_SelectedSubmesh}" : "All Submeshes";
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

        private Mesh m_CurrentFrameMesh;

        private void OnGUI()
        {
            if (Event.current.type == EventType.Layout)
            {
                m_CurrentFrameMesh = GetCurrentTargetMesh();
            }
            Mesh mesh = m_CurrentFrameMesh ?? GetCurrentTargetMesh();

            Event evt = Event.current;
            Rect windowRect = new Rect(0, 0, position.width, position.height);
            bool isHovered = windowRect.Contains(evt.mousePosition);

            // Handle Undo/Redo only when mouse is hovering over the UV Preview Window
            if (mesh != null && isHovered)
            {
                UVEditorCore.HandleUndoRedoEvents(evt, isHovered, mesh, m_ActiveChannel, m_SelectedVertices, () =>
                {
                    Repaint();
                    SceneView.RepaintAll();
                });
            }

            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

            // Target selector toolbar
            DrawTargetToolbar();

            if (mesh == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject with a MeshFilter / SkinnedMeshRenderer, or select a Mesh asset in the Project window.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            // Mesh info
            DrawMeshHeader(mesh);

            // Channel toolbar
            DrawChannelToolbar(mesh);

            // Material & Texture tabs
            DrawMaterialTextureTabs();

            // Selection toolbar
            DrawSelectionToolbar(mesh);

            // UV Preview Canvas (takes dynamic height based on window width)
            DrawPreviewSection(mesh);

            EditorGUILayout.Space(6);

            // Display & Visual Options Foldout
            DrawDisplaySettings();

            EditorGUILayout.Space(6);

            // Quick Fix Options Foldout
            DrawQuickFixSection(mesh);

            EditorGUILayout.EndScrollView();
        }

        private void DrawTargetToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                m_AutoSyncSelection = GUILayout.Toggle(m_AutoSyncSelection, "Auto-Sync Selection", EditorStyles.toolbarButton, GUILayout.Width(130));

                GUILayout.Space(6);

                UnityEngine.Object currentObj = m_TargetGameObject != null ? (UnityEngine.Object)m_TargetGameObject : m_DirectMesh;
                UnityEngine.Object pickedObj = EditorGUILayout.ObjectField(currentObj, typeof(UnityEngine.Object), true, GUILayout.Width(180));

                if (pickedObj != currentObj)
                {
                    if (pickedObj is GameObject go)
                    {
                        m_TargetGameObject = go;
                        m_DirectMesh = null;
                    }
                    else if (pickedObj is Mesh m)
                    {
                        m_DirectMesh = m;
                        m_TargetGameObject = null;
                    }
                    else if (pickedObj == null)
                    {
                        m_TargetGameObject = null;
                        m_DirectMesh = null;
                    }
                }

                GUILayout.FlexibleSpace();

                bool hasComponent = m_TargetGameObject != null && m_TargetGameObject.GetComponent<UVInspectorTool>() != null;
                using (new EditorGUI.DisabledScope(m_TargetGameObject == null || hasComponent))
                {
                    if (GUILayout.Button(hasComponent ? "In Inspector ✓" : "+ Add to Inspector", EditorStyles.toolbarButton, GUILayout.Width(120)))
                    {
                        if (m_TargetGameObject != null && !hasComponent)
                        {
                            Undo.AddComponent<UVInspectorTool>(m_TargetGameObject);
                        }
                    }
                }
            }
        }

        private void DrawMeshHeader(Mesh mesh)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"<b>{mesh.name}</b>", new GUIStyle(EditorStyles.boldLabel) { richText = true });
                    GUILayout.FlexibleSpace();

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

                EditorGUILayout.LabelField($"{mesh.vertexCount:N0} vertices | {mesh.triangles.Length / 3:N0} triangles | {mesh.subMeshCount} submesh");
            }
        }

        private void DrawChannelToolbar(Mesh mesh)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Channel:", EditorStyles.miniBoldLabel, GUILayout.Width(55));

                for (int ch = 0; ch < 4; ch++)
                {
                    bool hasUV = UVEditorCore.HasUVs(mesh, ch);
                    string label = hasUV ? $"UV{ch} •" : $"UV{ch}";
                    bool isSelected = m_ActiveChannel == ch;

                    GUI.backgroundColor = isSelected ? new Color(0.35f, 0.75f, 1f) : Color.white;
                    if (GUILayout.Toggle(isSelected, label, EditorStyles.toolbarButton, GUILayout.Width(54)) && !isSelected)
                    {
                        m_ActiveChannel = ch;
                    }
                    GUI.backgroundColor = Color.white;
                }

                GUILayout.FlexibleSpace();

                if (mesh.subMeshCount > 1)
                {
                    string[] submeshNames = new string[mesh.subMeshCount + 1];
                    submeshNames[0] = "All Submeshes";
                    for (int i = 0; i < mesh.subMeshCount; i++) submeshNames[i + 1] = $"Submesh {i}";

                    int currentIdx = m_SelectedSubmesh + 1;
                    int newIdx = EditorGUILayout.Popup(currentIdx, submeshNames, EditorStyles.toolbarPopup, GUILayout.Width(110));
                    if (newIdx != currentIdx)
                    {
                        m_SelectedSubmesh = newIdx - 1;
                        if (newIdx > 0)
                        {
                            m_ActiveMaterialIndex = newIdx - 1;
                            m_ActiveTextureIndex = 0;
                            m_BackgroundMode = UVBackgroundMode.MaterialTexture;
                            m_SelectedVertices.Clear();
                        }
                        UVEditorCore.InvalidateCache();
                        Repaint();
                    }
                }
            }
        }

        public void SelectAll(Mesh mesh, int channel)
        {
            m_SelectedVertices.Clear();
            if (mesh == null) return;
            if (m_SelectedSubmesh >= 0 && m_SelectedSubmesh < mesh.subMeshCount)
            {
                int[] tris = UVEditorCore.GetTriangles(mesh, m_SelectedSubmesh);
                for (int i = 0; i < tris.Length; i++) m_SelectedVertices.Add(tris[i]);
            }
            else
            {
                int count = mesh.vertexCount;
                for (int i = 0; i < count; i++) m_SelectedVertices.Add(i);
            }
        }

        public void ClearSelection()
        {
            m_SelectedVertices.Clear();
        }

        public void InvertSelection(Mesh mesh)
        {
            if (mesh == null) return;
            HashSet<int> inverted = new HashSet<int>();
            if (m_SelectedSubmesh >= 0 && m_SelectedSubmesh < mesh.subMeshCount)
            {
                int[] tris = UVEditorCore.GetTriangles(mesh, m_SelectedSubmesh);
                for (int i = 0; i < tris.Length; i++)
                {
                    int v = tris[i];
                    if (!m_SelectedVertices.Contains(v)) inverted.Add(v);
                }
            }
            else
            {
                int count = mesh.vertexCount;
                for (int i = 0; i < count; i++)
                {
                    if (!m_SelectedVertices.Contains(i)) inverted.Add(i);
                }
            }
            m_SelectedVertices.Clear();
            m_SelectedVertices.UnionWith(inverted);
        }

        private void DrawSelectionToolbar(Mesh mesh)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // Canvas Tool: Select / Move / Pan
                GUILayout.Label("Tool:", EditorStyles.miniBoldLabel, GUILayout.Width(35));
                bool isSelect = m_ActiveCanvasTool == CanvasTool.Select;
                GUI.backgroundColor = isSelect ? new Color(0.35f, 0.75f, 1f) : Color.white;
                if (GUILayout.Toggle(isSelect, "Select", EditorStyles.toolbarButton, GUILayout.Width(50)) && !isSelect)
                {
                    m_ActiveCanvasTool = CanvasTool.Select;
                }
                bool isMove = m_ActiveCanvasTool == CanvasTool.Move;
                GUI.backgroundColor = isMove ? new Color(0.35f, 0.75f, 1f) : Color.white;
                if (GUILayout.Toggle(isMove, "Move", EditorStyles.toolbarButton, GUILayout.Width(46)) && !isMove)
                {
                    m_ActiveCanvasTool = CanvasTool.Move;
                }
                bool isPan = m_ActiveCanvasTool == CanvasTool.Pan;
                GUI.backgroundColor = isPan ? new Color(0.35f, 0.75f, 1f) : Color.white;
                if (GUILayout.Toggle(isPan, "Pan", EditorStyles.toolbarButton, GUILayout.Width(40)) && !isPan)
                {
                    m_ActiveCanvasTool = CanvasTool.Pan;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.Space(6);

                // Selection Mode: Island / Quad / Face / Vertex
                GUILayout.Label("Mode:", EditorStyles.miniBoldLabel, GUILayout.Width(38));

                bool isIsland = m_SelectionMode == UVSelectionMode.Island;
                GUI.backgroundColor = isIsland ? new Color(1f, 0.85f, 0.35f) : Color.white;
                if (GUILayout.Toggle(isIsland, new GUIContent("Island", "Select connected UV Island (Hotkey: L)"), EditorStyles.toolbarButton, GUILayout.Width(50)) && !isIsland)
                {
                    m_SelectionMode = UVSelectionMode.Island;
                }

                bool isQuad = m_SelectionMode == UVSelectionMode.Quad;
                GUI.backgroundColor = isQuad ? new Color(1f, 0.85f, 0.35f) : Color.white;
                if (GUILayout.Toggle(isQuad, new GUIContent("Quad", "Select whole connected Quad (2 triangles)"), EditorStyles.toolbarButton, GUILayout.Width(46)) && !isQuad)
                {
                    m_SelectionMode = UVSelectionMode.Quad;
                }

                bool isFace = m_SelectionMode == UVSelectionMode.Face;
                GUI.backgroundColor = isFace ? new Color(1f, 0.85f, 0.35f) : Color.white;
                if (GUILayout.Toggle(isFace, new GUIContent("Face", "Select Face / Triangle (Alt+Click for single tri)"), EditorStyles.toolbarButton, GUILayout.Width(42)) && !isFace)
                {
                    m_SelectionMode = UVSelectionMode.Face;
                }

                bool isVertex = m_SelectionMode == UVSelectionMode.Vertex;
                GUI.backgroundColor = isVertex ? new Color(1f, 0.85f, 0.35f) : Color.white;
                if (GUILayout.Toggle(isVertex, new GUIContent("Vertex", "Select individual UV Vertex"), EditorStyles.toolbarButton, GUILayout.Width(48)) && !isVertex)
                {
                    m_SelectionMode = UVSelectionMode.Vertex;
                }
                GUI.backgroundColor = Color.white;

                bool isConn = m_SelectConnectedFBX;
                GUI.backgroundColor = isConn ? new Color(0.4f, 0.85f, 0.45f) : Color.white;
                if (GUILayout.Toggle(isConn, new GUIContent("🔗 FBX", "Select connected vertices also (like in original FBX / 3D model)"), EditorStyles.toolbarButton, GUILayout.Width(50)) != isConn)
                {
                    m_SelectConnectedFBX = !isConn;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.Space(6);

                var overlapInfo = UVEditorCore.GetIslandOverlapInfo(mesh, m_ActiveChannel, m_SelectedSubmesh);
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
                int selCount = m_SelectedVertices.Count;
                if (selCount > 0)
                {
                    GUILayout.Label($"<color=#ffd24d>★ {selCount:N0} verts</color>", new GUIStyle(EditorStyles.miniBoldLabel) { richText = true });
                }

                if (GUILayout.Button("All", EditorStyles.toolbarButton, GUILayout.Width(32)))
                {
                    SelectAll(mesh, m_ActiveChannel);
                    UVEditorCore.InvalidateCache();
                    Repaint();
                }

                if (GUILayout.Button("None", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    ClearSelection();
                    UVEditorCore.InvalidateCache();
                    Repaint();
                }

                if (GUILayout.Button("Invert", EditorStyles.toolbarButton, GUILayout.Width(44)))
                {
                    InvertSelection(mesh);
                    UVEditorCore.InvalidateCache();
                    Repaint();
                }
            }
        }

        private void DrawPreviewSection(Mesh mesh)
        {
            Vector2[] liveUVs = null;

            // Window-responsive preview height
            float availableWidth = position.width - 24;
            float previewHeight = Mathf.Clamp(availableWidth * 0.75f, 240f, 500f);

            Rect canvasRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(previewHeight), GUILayout.ExpandWidth(true));
            Texture2D matTex = GetTargetMaterialTexture();

            UVEditorCore.DrawPreviewGUI(
                canvasRect,
                mesh,
                m_ActiveChannel,
                m_SelectedSubmesh,
                m_BackgroundMode,
                m_CustomTexture,
                matTex,
                m_TextureAlpha,
                m_RepeatTexture,
                m_WireColor,
                m_FaceFillColor,
                m_FlippedFaceColor,
                m_ShowWireframe,
                m_ShowFaces,
                m_ShowVertices,
                m_ShowFlippedFaces,
                m_ShowGrid,
                m_ShowUnitBounds,
                ref m_ViewCenter,
                ref m_ViewZoom,
                liveUVs,
                m_SelectedVertices,
                m_SelectionMode,
                m_ActiveCanvasTool,
                () => {
                    Repaint();
                    SceneView.RepaintAll();
                },
                newUVs => ApplyUVFix(mesh, _ => newUVs, "Move Selected UVs"),
                () => Repaint(),
                m_ShowOverlapIndicators,
                m_OverlapHighlightColor,
                m_SelectConnectedFBX
            );

            // Controls under canvas
            Vector2[] displayUVs = liveUVs ?? UVEditorCore.GetUVs(mesh, m_ActiveChannel);
            UVEditorCore.CalculateUVBounds(displayUVs, out Vector2 minUV, out Vector2 maxUV);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool canUndo = UVEditorCore.UVHistory.CanUndo(mesh, m_ActiveChannel);
                bool canRedo = UVEditorCore.UVHistory.CanRedo(mesh, m_ActiveChannel);
                string undoName = UVEditorCore.UVHistory.GetUndoName(mesh, m_ActiveChannel);
                string redoName = UVEditorCore.UVHistory.GetRedoName(mesh, m_ActiveChannel);

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
                    m_ViewCenter = new Vector2(0.5f, 0.5f);
                    m_ViewZoom = 1.0f;
                }

                if (GUILayout.Button("Focus Bounds", EditorStyles.toolbarButton, GUILayout.Width(88)))
                {
                    Vector2 center = (minUV + maxUV) * 0.5f;
                    float maxDim = Mathf.Max(0.01f, Mathf.Max(maxUV.x - minUV.x, maxUV.y - minUV.y));
                    m_ViewCenter = center;
                    m_ViewZoom = Mathf.Clamp(1.1f / maxDim, 0.1f, 50f);
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label($"Bounds: [{minUV.x:F2}, {minUV.y:F2}]..[{maxUV.x:F2}, {maxUV.y:F2}]", EditorStyles.miniLabel);
            }
        }

        private void DrawDisplaySettings()
        {
            m_DisplayOptionsFoldout = EditorGUILayout.Foldout(m_DisplayOptionsFoldout, "Display & Visual Options", true, EditorStyles.foldoutHeader);
            if (!m_DisplayOptionsFoldout) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                m_BackgroundMode = (UVBackgroundMode)EditorGUILayout.EnumPopup("Background Mode", m_BackgroundMode);

                if (m_BackgroundMode == UVBackgroundMode.CustomTexture)
                {
                    m_CustomTexture = (Texture2D)EditorGUILayout.ObjectField("Custom Texture", m_CustomTexture, typeof(Texture2D), false);
                }

                if (m_BackgroundMode == UVBackgroundMode.MaterialTexture || m_BackgroundMode == UVBackgroundMode.CustomTexture)
                {
                    m_TextureAlpha = EditorGUILayout.Slider("Texture Opacity", m_TextureAlpha, 0f, 1f);
                    m_RepeatTexture = EditorGUILayout.Toggle("Repeat / Tile Texture", m_RepeatTexture);
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    m_ShowWireframe = EditorGUILayout.ToggleLeft("Wireframe", m_ShowWireframe, GUILayout.Width(90));
                    m_ShowFaces = EditorGUILayout.ToggleLeft("Face Fill", m_ShowFaces, GUILayout.Width(80));
                    m_ShowFlippedFaces = EditorGUILayout.ToggleLeft("Flipped Faces", m_ShowFlippedFaces, GUILayout.Width(100));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    m_ShowVertices = EditorGUILayout.ToggleLeft("Vertices", m_ShowVertices, GUILayout.Width(90));
                    m_ShowGrid = EditorGUILayout.ToggleLeft("Grid", m_ShowGrid, GUILayout.Width(80));
                    m_ShowUnitBounds = EditorGUILayout.ToggleLeft("[0,1] Bounds", m_ShowUnitBounds, GUILayout.Width(100));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    m_ShowOverlapIndicators = EditorGUILayout.ToggleLeft("Overlap Indicators", m_ShowOverlapIndicators, GUILayout.Width(130));
                }

                EditorGUILayout.Space(4);

                m_WireColor = EditorGUILayout.ColorField("Wireframe Color", m_WireColor);
                if (m_ShowFaces) m_FaceFillColor = EditorGUILayout.ColorField("Face Fill Color", m_FaceFillColor);
                if (m_ShowFlippedFaces) m_FlippedFaceColor = EditorGUILayout.ColorField("Flipped Highlight Color", m_FlippedFaceColor);
                if (m_ShowOverlapIndicators) m_OverlapHighlightColor = EditorGUILayout.ColorField("Overlap Highlight Color", m_OverlapHighlightColor);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("3D Scene View Highlight", EditorStyles.boldLabel);
                m_ShowSceneHighlight = EditorGUILayout.Toggle("Show in Scene View", m_ShowSceneHighlight);
                if (m_ShowSceneHighlight)
                {
                    EditorGUI.indentLevel++;
                    m_ShowSceneUVLabels = EditorGUILayout.Toggle("Show UV Labels in Scene", m_ShowSceneUVLabels);
                    m_SceneHighlightColor = EditorGUILayout.ColorField("Scene Highlight Color", m_SceneHighlightColor);
                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawQuickFixSection(Mesh mesh)
        {
            m_QuickFixFoldout = EditorGUILayout.Foldout(m_QuickFixFoldout, "Quick Fix Options", true, EditorStyles.foldoutHeader);
            if (!m_QuickFixFoldout) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int selCount = m_SelectedVertices.Count;

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
                    bool canUndo = UVEditorCore.UVHistory.CanUndo(mesh, m_ActiveChannel);
                    bool canRedo = UVEditorCore.UVHistory.CanRedo(mesh, m_ActiveChannel);
                    string undoName = UVEditorCore.UVHistory.GetUndoName(mesh, m_ActiveChannel);
                    string redoName = UVEditorCore.UVHistory.GetRedoName(mesh, m_ActiveChannel);

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
                        int[] tris = UVEditorCore.GetTriangles(mesh, m_SelectedSubmesh);
                        ApplyUVFix(mesh, uvs => UVEditorCore.FlipSelectedUVOrigin(uvs, m_FlipOriginAxis, m_SelectedVertices, tris), "Flip Selected UV Origin");
                    }
                    GUI.backgroundColor = Color.white;

                    m_FlipOriginAxis = (UVFlipAxis)EditorGUILayout.EnumPopup(m_FlipOriginAxis, GUILayout.Width(82), GUILayout.Height(28));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("↕ Flip V (Selected Origin)", "Flips vertically around the center of the selected UV in-place"), GUILayout.Height(22)))
                    {
                        int[] tris = UVEditorCore.GetTriangles(mesh, m_SelectedSubmesh);
                        ApplyUVFix(mesh, uvs => UVEditorCore.FlipSelectedUVOrigin(uvs, UVFlipAxis.Vertical, m_SelectedVertices, tris), "Flip Selected UV Origin V");
                    }
                    if (GUILayout.Button(new GUIContent("↔ Flip U (Selected Origin)", "Flips horizontally around the center of the selected UV in-place"), GUILayout.Height(22)))
                    {
                        int[] tris = UVEditorCore.GetTriangles(mesh, m_SelectedSubmesh);
                        ApplyUVFix(mesh, uvs => UVEditorCore.FlipSelectedUVOrigin(uvs, UVFlipAxis.Horizontal, m_SelectedVertices, tris), "Flip Selected UV Origin U");
                    }
                }

                EditorGUILayout.Space(2);

                // Section: Canvas Center Flips (around canvas 0.5 center)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
                    if (GUILayout.Button(new GUIContent("↕ Flip V (Canvas Center)", "Flips vertically across canvas center [0.5] (Fix inverted Y imports)"), GUILayout.Height(24)))
                    {
                        ApplyUVFix(mesh, uvs => UVEditorCore.FlipV(uvs, true, m_SelectedVertices), "Flip V (Canvas Center)");
                    }
                    GUI.backgroundColor = Color.white;

                    if (GUILayout.Button(new GUIContent("↔ Flip U (Canvas Center)", "Flips horizontally across canvas center [0.5] (Mirror X)"), GUILayout.Height(24)))
                    {
                        ApplyUVFix(mesh, uvs => UVEditorCore.FlipU(uvs, true, m_SelectedVertices), "Flip U (Canvas Center)");
                    }
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("↻ Rotate 90° CW"))
                    {
                        Vector2[] uvs = UVEditorCore.GetUVs(mesh, m_ActiveChannel);
                        Vector2 pivot = GetPivotCoordinates(uvs, m_SelectedVertices);
                        ApplyUVFix(mesh, u => UVEditorCore.Rotate90(u, true, pivot, m_SelectedVertices), "Rotate 90 CW");
                    }
                    if (GUILayout.Button("↺ Rotate 90° CCW"))
                    {
                        Vector2[] uvs = UVEditorCore.GetUVs(mesh, m_ActiveChannel);
                        Vector2 pivot = GetPivotCoordinates(uvs, m_SelectedVertices);
                        ApplyUVFix(mesh, u => UVEditorCore.Rotate90(u, false, pivot, m_SelectedVertices), "Rotate 90 CCW");
                    }
                    if (GUILayout.Button("Rotate 180°"))
                    {
                        Vector2[] uvs = UVEditorCore.GetUVs(mesh, m_ActiveChannel);
                        Vector2 pivot = GetPivotCoordinates(uvs, m_SelectedVertices);
                        ApplyUVFix(mesh, u => UVEditorCore.Rotate(u, 180f, pivot, m_SelectedVertices), "Rotate 180");
                    }
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Fit to [0, 1] (Keep Aspect)"))
                    {
                        ApplyUVFix(mesh, u => UVEditorCore.FitPreserveAspect(u, m_SelectedVertices), "Fit UV Aspect");
                    }
                    if (GUILayout.Button("Normalize to [0, 1] (Stretch)"))
                    {
                        ApplyUVFix(mesh, u => UVEditorCore.NormalizeUVs(u, m_SelectedVertices), "Normalize UVs");
                    }
                    if (GUILayout.Button("Center (to 0.5, 0.5)"))
                    {
                        ApplyUVFix(mesh, u => UVEditorCore.CenterUVs(u, m_SelectedVertices), "Center UVs");
                    }
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Clamp [0, 1]"))
                    {
                        ApplyUVFix(mesh, u => UVEditorCore.Clamp01(u, m_SelectedVertices), "Clamp UVs");
                    }
                    if (GUILayout.Button("Wrap [0, 1] (Modulo)"))
                    {
                        ApplyUVFix(mesh, u => UVEditorCore.Wrap01(u, m_SelectedVertices), "Wrap UVs");
                    }
                }
            }
        }

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

        private Mesh EnsureInstance(Mesh mesh)
        {
            UnityEngine.Object owner = m_TargetGameObject != null ? (UnityEngine.Object)m_TargetGameObject : this;
            return UVEditorCore.EnsureMeshInstance(owner, mesh, newMesh =>
            {
                SetCurrentTargetMesh(newMesh);
            });
        }

        private void ApplyUVFix(Mesh mesh, Func<Vector2[], Vector2[]> operation, string undoName)
        {
            Mesh instMesh = EnsureInstance(mesh);
            int channel = m_ActiveChannel;

            Vector2[] currentUVs = UVEditorCore.GetUVs(instMesh, channel);
            UVEditorCore.UVHistory.EnsureInitialState(instMesh, channel, currentUVs, "Initial State", m_SelectedVertices);

            Vector2[] fixedUVs = operation(currentUVs);

            UVEditorCore.SetUVs(instMesh, channel, fixedUVs);
            EditorUtility.SetDirty(instMesh);
            Repaint();

            UVEditorCore.UVHistory.RecordStep(instMesh, channel, fixedUVs, undoName, m_SelectedVertices);
        }

        private void PerformUndo(Mesh mesh)
        {
            Mesh instMesh = EnsureInstance(mesh);
            if (instMesh == null) return;
            if (UVEditorCore.UVHistory.PerformUndo(instMesh, m_ActiveChannel, m_SelectedVertices, () =>
            {
                EditorUtility.SetDirty(instMesh);
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
            if (UVEditorCore.UVHistory.PerformRedo(instMesh, m_ActiveChannel, m_SelectedVertices, () =>
            {
                EditorUtility.SetDirty(instMesh);
                Repaint();
                SceneView.RepaintAll();
            }))
            {
                Repaint();
                SceneView.RepaintAll();
            }
        }
    }
}
#endif

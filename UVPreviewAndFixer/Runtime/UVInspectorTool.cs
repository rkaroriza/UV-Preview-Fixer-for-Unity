using System.Collections.Generic;
using UnityEngine;

namespace UVTool
{
    public enum UVBackgroundMode
    {
        DarkGrid,
        LightGrid,
        Checkerboard,
        MaterialTexture,
        CustomTexture
    }

    public enum UVSelectionMode
    {
        Island = 0,
        Quad = 1,
        Face = 2,
        Vertex = 3
    }

    public enum CanvasTool
    {
        Select,
        Move,
        Pan
    }

    public enum UVFlipAxis
    {
        Vertical = 0,   // Flip Y (Up/Down)
        Horizontal = 1, // Flip X (Left/Right)
        Both = 2        // Flip X & Y (Invert)
    }

    [System.Serializable]
    public struct MaterialTextureSlot
    {
        public string propertyName;
        public string displayName;
        public Texture2D texture;
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Mesh/UV Preview & Fixer")]
    public class UVInspectorTool : MonoBehaviour
    {
        [Header("Original Mesh Reference")]
        [SerializeField] private Mesh m_OriginalMesh;

        [Header("Material & Texture Tabs")]
        [SerializeField] private int m_ActiveMaterialIndex = 0;
        [SerializeField] private int m_ActiveTextureIndex = 0;

        [Header("Preview Settings")]
        [SerializeField] private int m_ActiveChannel = 0;
        [SerializeField] private int m_SelectedSubmesh = -1; // -1 for all
        [SerializeField] private UVBackgroundMode m_BackgroundMode = UVBackgroundMode.MaterialTexture;
        [SerializeField] private Texture2D m_CustomTexture;
        [SerializeField] [Range(0f, 1f)] private float m_TextureAlpha = 0.85f;
        [SerializeField] private Color m_WireColor = new Color(0.2f, 0.9f, 1f, 0.85f);
        [SerializeField] private Color m_FlippedFaceColor = new Color(0.2f, 0.6f, 1f, 0.15f);
        [SerializeField] private Color m_FaceFillColor = new Color(0.2f, 0.6f, 1f, 0f); // Default transparent (no inside color)
        [SerializeField] private bool m_ShowWireframe = true;
        [SerializeField] private bool m_ShowFaces = false;
        [SerializeField] private bool m_ShowVertices = false;
        [SerializeField] private bool m_ShowFlippedFaces = false;
        [SerializeField] private bool m_ShowGrid = true;
        [SerializeField] private bool m_ShowUnitBounds = true;
        [SerializeField] private bool m_RepeatTexture = false;
        [SerializeField] private bool m_ShowOverlapIndicators = true;
        [SerializeField] private Color m_OverlapHighlightColor = new Color(1.0f, 0.55f, 0.15f, 0.95f);

        [Header("Scene View 3D Highlight")]
        [SerializeField] private bool m_ShowSceneHighlight = true;
        [SerializeField] private bool m_ShowSceneUVLabels = true;
        [SerializeField] private Color m_SceneHighlightColor = new Color(1.0f, 0.75f, 0.18f, 0.45f);

        [Header("Canvas View")]
        [SerializeField] private Vector2 m_ViewCenter = new Vector2(0.5f, 0.5f);
        [SerializeField] private float m_ViewZoom = 1.0f;
        [SerializeField] private float m_PreviewHeight = 320f;

        [Header("Selection")]
        [SerializeField] private UVSelectionMode m_SelectionMode = UVSelectionMode.Island;
        [SerializeField] private CanvasTool m_ActiveCanvasTool = CanvasTool.Select;
        [SerializeField] private UVFlipAxis m_FlipOriginAxis = UVFlipAxis.Vertical;
        [SerializeField] private bool m_SelectConnectedFBX = true;
        private readonly HashSet<int> m_SelectedVertices = new HashSet<int>();

        // Public Properties
        public Mesh originalMesh
        {
            get => m_OriginalMesh;
            set => m_OriginalMesh = value;
        }

        public int activeChannel
        {
            get => m_ActiveChannel;
            set => m_ActiveChannel = Mathf.Clamp(value, 0, 3);
        }

        public int selectedSubmesh
        {
            get => m_SelectedSubmesh;
            set => m_SelectedSubmesh = value;
        }

        public UVBackgroundMode backgroundMode
        {
            get => m_BackgroundMode;
            set => m_BackgroundMode = value;
        }

        public Texture2D customTexture
        {
            get => m_CustomTexture;
            set => m_CustomTexture = value;
        }

        public float textureAlpha
        {
            get => m_TextureAlpha;
            set => m_TextureAlpha = Mathf.Clamp01(value);
        }

        public Color wireColor
        {
            get => m_WireColor;
            set => m_WireColor = value;
        }

        public Color flippedFaceColor
        {
            get => m_FlippedFaceColor;
            set => m_FlippedFaceColor = value;
        }

        public Color faceFillColor
        {
            get => m_FaceFillColor;
            set => m_FaceFillColor = value;
        }

        public bool showWireframe
        {
            get => m_ShowWireframe;
            set => m_ShowWireframe = value;
        }

        public bool showFaces
        {
            get => m_ShowFaces;
            set => m_ShowFaces = value;
        }

        public bool showVertices
        {
            get => m_ShowVertices;
            set => m_ShowVertices = value;
        }

        public bool showFlippedFaces
        {
            get => m_ShowFlippedFaces;
            set => m_ShowFlippedFaces = value;
        }

        public bool showGrid
        {
            get => m_ShowGrid;
            set => m_ShowGrid = value;
        }

        public bool showUnitBounds
        {
            get => m_ShowUnitBounds;
            set => m_ShowUnitBounds = value;
        }

        public bool repeatTexture
        {
            get => m_RepeatTexture;
            set => m_RepeatTexture = value;
        }

        public bool showOverlapIndicators
        {
            get => m_ShowOverlapIndicators;
            set => m_ShowOverlapIndicators = value;
        }

        public Color overlapHighlightColor
        {
            get => m_OverlapHighlightColor;
            set => m_OverlapHighlightColor = value;
        }

        public bool showSceneHighlight
        {
            get => m_ShowSceneHighlight;
            set => m_ShowSceneHighlight = value;
        }

        public bool showSceneUVLabels
        {
            get => m_ShowSceneUVLabels;
            set => m_ShowSceneUVLabels = value;
        }

        public Color sceneHighlightColor
        {
            get => m_SceneHighlightColor;
            set => m_SceneHighlightColor = value;
        }

        public Vector2 viewCenter
        {
            get => m_ViewCenter;
            set => m_ViewCenter = value;
        }

        public float viewZoom
        {
            get => m_ViewZoom;
            set => m_ViewZoom = Mathf.Clamp(value, 0.05f, 100f);
        }

        public float previewHeight
        {
            get => m_PreviewHeight;
            set => m_PreviewHeight = Mathf.Clamp(value, 180f, 600f);
        }

        public int activeMaterialIndex
        {
            get => m_ActiveMaterialIndex;
            set => m_ActiveMaterialIndex = value;
        }

        public int activeTextureIndex
        {
            get => m_ActiveTextureIndex;
            set => m_ActiveTextureIndex = value;
        }

        public UVSelectionMode selectionMode
        {
            get => m_SelectionMode;
            set => m_SelectionMode = value;
        }

        public CanvasTool activeCanvasTool
        {
            get => m_ActiveCanvasTool;
            set => m_ActiveCanvasTool = value;
        }

        public UVFlipAxis flipOriginAxis
        {
            get => m_FlipOriginAxis;
            set => m_FlipOriginAxis = value;
        }

        public bool selectConnectedFBX
        {
            get => m_SelectConnectedFBX;
            set => m_SelectConnectedFBX = value;
        }

        public HashSet<int> selectedVertices => m_SelectedVertices;

        public void SelectAll(Mesh mesh, int channel)
        {
            m_SelectedVertices.Clear();
            if (mesh == null) return;
            if (m_SelectedSubmesh >= 0 && m_SelectedSubmesh < mesh.subMeshCount)
            {
                int[] tris = mesh.GetTriangles(m_SelectedSubmesh);
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
                int[] tris = mesh.GetTriangles(m_SelectedSubmesh);
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

        private void Reset()
        {
            CacheOriginalMesh();
        }

        private void OnValidate()
        {
            if (m_OriginalMesh == null)
            {
                CacheOriginalMesh();
            }

            // Ensure no red color in UV preview
            if (m_FlippedFaceColor.r > 0.7f && m_FlippedFaceColor.g < 0.4f)
            {
                m_ShowFlippedFaces = false;
                m_FlippedFaceColor = new Color(0.2f, 0.6f, 1f, 0.15f);
            }
        }

        /// <summary>
        /// Stores the starting mesh reference so changes can be reverted anytime.
        /// </summary>
        public void CacheOriginalMesh()
        {
            Mesh current = GetCurrentSharedMesh();
            if (current != null && m_OriginalMesh == null)
            {
                m_OriginalMesh = current;
            }
        }

        public Mesh GetCurrentSharedMesh()
        {
            if (TryGetComponent<MeshFilter>(out var mf))
            {
                return mf.sharedMesh;
            }
            if (TryGetComponent<SkinnedMeshRenderer>(out var smr))
            {
                return smr.sharedMesh;
            }
            return null;
        }

        public void SetCurrentSharedMesh(Mesh mesh)
        {
            if (TryGetComponent<MeshFilter>(out var mf))
            {
                mf.sharedMesh = mesh;
            }
            else if (TryGetComponent<SkinnedMeshRenderer>(out var smr))
            {
                smr.sharedMesh = mesh;
            }
        }

        public static string GetFriendlyTextureName(string propName)
        {
            if (string.IsNullOrEmpty(propName)) return "Texture";

            return propName switch
            {
                "_BaseMap" => "Albedo (Base)",
                "_MainTex" => "Albedo (Main)",
                "_BumpMap" => "Normal",
                "_NormalMap" => "Normal",
                "_DetailNormalMap" => "Detail Normal",
                "_MetallicGlossMap" => "Metallic",
                "_SpecGlossMap" => "Specular",
                "_OcclusionMap" => "Occlusion (AO)",
                "_EmissionMap" => "Emission",
                "_DetailAlbedoMap" => "Detail Albedo",
                "_HeightMap" => "Height",
                "_ParallaxMap" => "Height",
                "_MaskMap" => "Mask",
                "_LightMap" => "Lightmap",
                _ => propName.TrimStart('_')
            };
        }

        private static Material s_CachedSlotMat;
        private static List<MaterialTextureSlot> s_CachedSlots;

        public static List<MaterialTextureSlot> GetMaterialTextureSlots(Material mat)
        {
            if (mat == null) return new List<MaterialTextureSlot>();
            if (s_CachedSlotMat == mat && s_CachedSlots != null) return s_CachedSlots;

            List<MaterialTextureSlot> result = new List<MaterialTextureSlot>();
            string[] propNames = mat.GetTexturePropertyNames();
            HashSet<Texture2D> addedTextures = new HashSet<Texture2D>();

            foreach (string prop in propNames)
            {
                Texture tex = mat.GetTexture(prop);
                if (tex is Texture2D t2d && t2d != null && !addedTextures.Contains(t2d))
                {
                    result.Add(new MaterialTextureSlot
                    {
                        propertyName = prop,
                        displayName = GetFriendlyTextureName(prop),
                        texture = t2d
                    });
                    addedTextures.Add(t2d);
                }
            }

            s_CachedSlotMat = mat;
            s_CachedSlots = result;
            return result;
        }

        public static void InvalidateMaterialCache()
        {
            s_CachedSlotMat = null;
            s_CachedSlots = null;
        }

        public Material[] GetTargetMaterials()
        {
            if (TryGetComponent<Renderer>(out var rend))
            {
                return rend.sharedMaterials;
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
            return GetMaterialTextureSlots(mat);
        }

        public Texture2D GetActiveMaterialTexture()
        {
            var slots = GetActiveMaterialTextureSlots();
            if (slots == null || slots.Count == 0)
            {
                // Fallback to legacy check if no TexturePropertyNames detected
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

        public Material GetFirstMaterial()
        {
            if (TryGetComponent<Renderer>(out var rend))
            {
                return rend.sharedMaterial;
            }
            return null;
        }

        public Texture2D GetMaterialTexture()
        {
            return GetActiveMaterialTexture();
        }

        public void ResetView()
        {
            m_ViewCenter = new Vector2(0.5f, 0.5f);
            m_ViewZoom = 1.0f;
        }
    }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UVTool.Editor
{
    public static class UVEditorCore
    {
        // Cache for preview rendering
        private static RenderTexture s_PreviewRT;
        private static Material s_PreviewMaterial;
        private static Texture2D s_CheckerTexture;

        // Cached 2D Geometry Arrays for Zero-Allocation Rendering
        private static Vector3[] s_Cached2DVertices;
        private static int[] s_CachedLineIndices;
        private static int[] s_CachedFillTris;
        private static int[] s_CachedFlippedTris;
        private static int[] s_CachedSelLineIndices;
        private static int[] s_CachedSelOutlineIndices;
        private static int[] s_CachedSelPointsIndices;

        // Zero-Allocation UV Array Cache
        private static Vector2[] s_CachedUVs;
        private static int s_CachedUVsMeshId;
        private static int s_CachedUVsChannel = -1;
        private static int s_CachedUVsVertexCount;
        private static readonly List<Vector2> s_ScratchUVList = new List<Vector2>();

        // Zero-Allocation Triangles Cache
        private static int[] s_CachedTriangles;
        private static int s_CachedTrianglesMeshId;
        private static int s_CachedTrianglesSubmesh = -999;

        // Zero-Allocation Scratch Collections
        private static readonly HashSet<int> s_ScratchSelIslandIds = new HashSet<int>();
        private static readonly Dictionary<long, int> s_ScratchEdgeUsage = new Dictionary<long, int>();
        private static readonly Dictionary<long, KeyValuePair<int, int>> s_ScratchEdgeMap = new Dictionary<long, KeyValuePair<int, int>>();

        // Cached 3D Mesh Vertices for Click / Marquee Selection
        private static Vector3[] s_CachedMeshVertices3D;
        private static int s_CachedMeshVertices3DMeshId;

        public static Vector3[] GetCachedMeshVertices(Mesh mesh)
        {
            if (mesh == null) return null;
            int id = mesh.GetInstanceID();
            if (s_CachedMeshVertices3D == null || s_CachedMeshVertices3DMeshId != id || s_CachedMeshVertices3D.Length != mesh.vertexCount)
            {
                s_CachedMeshVertices3D = mesh.vertices;
                s_CachedMeshVertices3DMeshId = id;
            }
            return s_CachedMeshVertices3D;
        }

        // Zero-Allocation Scene View 3D Highlight Cache
        private class SceneHighlightCache
        {
            public int targetInstanceId;
            public int meshInstanceId;
            public int submeshIndex;
            public int channel;
            public int selectionHash;
            public UVSelectionMode selectionMode;
            public bool showUVLabels;
            public bool isValid;

            public Vector3[] fillVertices;
            public Vector3[] boundaryVertices;
            public Vector3[] internalVertices;
            public Vector3[] vertexHandles;

            public Vector3 localCentroid;
            public Vector3 localNormal;
            public string badgeTitle;
            public string badgeUV;
            public int overlapCount;
            public int sharedGroupSize;

            public struct CornerChip
            {
                public Vector3 localPos;
                public string uvText;
            }
            public CornerChip[] cornerChips;
        }

        private static readonly SceneHighlightCache s_SceneCache = new SceneHighlightCache();
        private static GUIStyle s_SceneTitleStyle;
        private static GUIStyle s_SceneUVStyle;
        private static GUIStyle s_SceneCornerStyle;

        private static readonly List<Vector3> s_ScratchVerts = new List<Vector3>();
        private static readonly List<Vector3> s_ScratchNormals = new List<Vector3>();
        private static readonly List<Vector2> s_ScratchUVs = new List<Vector2>();
        private static readonly List<int> s_ScratchTriangles = new List<int>();
        private static readonly List<int> s_ScratchSubmeshTris = new List<int>();
        private static readonly List<int> s_ScratchSelTris = new List<int>();
        private static readonly List<int> s_ScratchBoundaryEdges = new List<int>();
        private static readonly List<int> s_ScratchInternalEdges = new List<int>();

        private static int s_CachedMeshInstanceId = 0;
        private static int s_CachedChannel = -1;
        private static int s_CachedSubmesh = -2;
        private static int s_CachedUVHash = 0;
        private static int s_CachedSelectionHash = -1;

        // Interaction state
        private static bool s_IsPanning = false;
        private static Vector2 s_PanStartMouse;
        private static Vector2 s_PanStartCenter;

        // Selection & Transformation interaction state
        private static bool s_IsMarqueeSelecting = false;
        private static Vector2 s_MarqueeStartMouse;
        private static Vector2 s_MarqueeCurrentMouse;
        private static bool s_HasDragMoved = false;

        // Move / Drag selected UVs state
        private static bool s_IsDraggingUVs = false;
        private static Vector2 s_DragUVStartMouse;
        private static Vector2[] s_DragOriginalUVs;
        private static Vector2 s_DragCurrentDeltaUV;
        private static Vector2 s_DragOriginalCentroid;
        private static bool s_HasUVMoved = false;

        #region Shader & Material Management

        public static Material GetPreviewMaterial()
        {
            if (s_PreviewMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/UVTool/PreviewShader");
                if (shader == null)
                {
                    // Fallback to internal colored shader if not found
                    shader = Shader.Find("Hidden/Internal-Colored");
                }
                if (shader != null)
                {
                    s_PreviewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
            }
            return s_PreviewMaterial;
        }

        public static Texture2D GetCheckerTexture()
        {
            if (s_CheckerTexture == null)
            {
                s_CheckerTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat
                };

                Color dark = new Color(0.18f, 0.18f, 0.20f, 1f);
                Color light = new Color(0.28f, 0.28f, 0.30f, 1f);

                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        bool isEven = ((x / 4) + (y / 4)) % 2 == 0;
                        s_CheckerTexture.SetPixel(x, y, isEven ? light : dark);
                    }
                }
                s_CheckerTexture.Apply();
            }
            return s_CheckerTexture;
        }

        private static void EnsureRenderTexture(int width, int height)
        {
            width = Mathf.Max(64, width);
            height = Mathf.Max(64, height);

            if (s_PreviewRT == null || s_PreviewRT.width != width || s_PreviewRT.height != height)
            {
                if (s_PreviewRT != null)
                {
                    s_PreviewRT.Release();
                    UnityEngine.Object.DestroyImmediate(s_PreviewRT);
                }

                s_PreviewRT = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    antiAliasing = 4
                };
                s_PreviewRT.Create();
            }
        }

        public static string GetFriendlyTextureName(string propName) => UVInspectorTool.GetFriendlyTextureName(propName);
        public static List<MaterialTextureSlot> GetMaterialTextureSlots(Material mat) => UVInspectorTool.GetMaterialTextureSlots(mat);

        #endregion

        #region Mesh & UV Access

        public static Vector2[] GetUVs(Mesh mesh, int channel)
        {
            if (mesh == null) return Array.Empty<Vector2>();

            int id = mesh.GetInstanceID();
            if (s_CachedUVs != null && s_CachedUVsMeshId == id && s_CachedUVsChannel == channel && s_CachedUVsVertexCount == mesh.vertexCount)
            {
                return s_CachedUVs;
            }

            if (channel < 0 || channel > 7 || !mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + channel))
            {
                return Array.Empty<Vector2>();
            }

            s_ScratchUVList.Clear();
            mesh.GetUVs(channel, s_ScratchUVList);
            s_CachedUVs = s_ScratchUVList.ToArray();
            s_CachedUVsMeshId = id;
            s_CachedUVsChannel = channel;
            s_CachedUVsVertexCount = mesh.vertexCount;
            return s_CachedUVs;
        }

        public static bool HasUVs(Mesh mesh, int channel)
        {
            if (mesh == null || channel < 0 || channel > 7) return false;
            return mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + channel);
        }

        public static void SetUVs(Mesh mesh, int channel, Vector2[] uvs)
        {
            if (mesh == null) return;
            mesh.SetUVs(channel, uvs);
            mesh.UploadMeshData(false);
            InvalidateCache();
        }

        public static void CalculateUVBounds(Vector2[] uvs, out Vector2 min, out Vector2 max)
        {
            CalculateUVBounds(uvs, null, out min, out max);
        }

        public static void CalculateUVBounds(Vector2[] uvs, HashSet<int> selectedIndices, out Vector2 min, out Vector2 max)
        {
            CalculateUVBounds(uvs, (IEnumerable<int>)selectedIndices, out min, out max);
        }

        public static void CalculateUVBounds(Vector2[] uvs, IEnumerable<int> selectedIndices, out Vector2 min, out Vector2 max)
        {
            if (uvs == null || uvs.Length == 0)
            {
                min = Vector2.zero;
                max = Vector2.one;
                return;
            }

            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);

            if (selectedIndices != null)
            {
                bool any = false;
                foreach (int idx in selectedIndices)
                {
                    if (idx >= 0 && idx < uvs.Length)
                    {
                        any = true;
                        Vector2 uv = uvs[idx];
                        if (uv.x < min.x) min.x = uv.x;
                        if (uv.x > max.x) max.x = uv.x;
                        if (uv.y < min.y) min.y = uv.y;
                        if (uv.y > max.y) max.y = uv.y;
                    }
                }
                if (any)
                {
                    if (min.x > max.x)
                    {
                        min = Vector2.zero;
                        max = Vector2.one;
                    }
                    return;
                }
            }

            for (int i = 0; i < uvs.Length; i++)
            {
                Vector2 uv = uvs[i];
                if (uv.x < min.x) min.x = uv.x;
                if (uv.x > max.x) max.x = uv.x;
                if (uv.y < min.y) min.y = uv.y;
                if (uv.y > max.y) max.y = uv.y;
            }

            if (min.x > max.x)
            {
                min = Vector2.zero;
                max = Vector2.one;
            }
        }

        public static int CountFlippedTriangles(Vector2[] uvs, int[] triangles)
        {
            if (uvs == null || triangles == null || triangles.Length < 3) return 0;

            int count = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                if (i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length) continue;

                Vector2 p0 = uvs[i0];
                Vector2 p1 = uvs[i1];
                Vector2 p2 = uvs[i2];

                float cross = (p1.x - p0.x) * (p2.y - p0.y) - (p1.y - p0.y) * (p2.x - p0.x);
                if (cross < -1e-6f)
                {
                    count++;
                }
            }
            return count;
        }

        public static int[] GetTriangles(Mesh mesh, int submeshIndex)
        {
            if (mesh == null) return Array.Empty<int>();

            int id = mesh.GetInstanceID();
            if (s_CachedTriangles != null && s_CachedTrianglesMeshId == id && s_CachedTrianglesSubmesh == submeshIndex)
            {
                return s_CachedTriangles;
            }

            if (submeshIndex >= 0 && submeshIndex < mesh.subMeshCount)
            {
                s_CachedTriangles = mesh.GetTriangles(submeshIndex);
            }
            else
            {
                s_CachedTriangles = mesh.triangles;
            }

            s_CachedTrianglesMeshId = id;
            s_CachedTrianglesSubmesh = submeshIndex;
            return s_CachedTriangles;
        }

        public static int GetSubmeshForVertices(Mesh mesh, ICollection<int> vertexIndices)
        {
            if (mesh == null || vertexIndices == null || vertexIndices.Count == 0) return -1;
            if (mesh.subMeshCount <= 1) return 0;

            HashSet<int> vSet = vertexIndices as HashSet<int> ?? new HashSet<int>(vertexIndices);
            int bestSubmesh = -1;
            int maxMatchCount = 0;

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] tris = GetTriangles(mesh, s);
                if (tris == null || tris.Length == 0) continue;

                int count = 0;
                for (int i = 0; i < tris.Length; i++)
                {
                    if (vSet.Contains(tris[i]))
                    {
                        count++;
                        if (count > maxMatchCount)
                        {
                            maxMatchCount = count;
                            bestSubmesh = s;
                        }
                    }
                }
            }
            return bestSubmesh;
        }

        private static int ComputeUVHash(Vector2[] uvs)
        {
            if (uvs == null || uvs.Length == 0) return 0;
            int hash = uvs.Length;
            // Sample points for quick hashing
            int step = Mathf.Max(1, uvs.Length / 16);
            for (int i = 0; i < uvs.Length; i += step)
            {
                hash = unchecked(hash * 31 + uvs[i].GetHashCode());
            }
            return hash;
        }

        public static void InvalidateCache()
        {
            s_CachedMeshInstanceId = 0;
            s_CachedChannel = -1;
            s_CachedSubmesh = -2;
            s_CachedUVHash = 0;
            s_CachedSelectionHash = -1;
            s_CachedMeshVertices3D = null;
            s_CachedMeshVertices3DMeshId = 0;
            s_CachedUVs = null;
            s_CachedUVsMeshId = 0;
            s_CachedUVsChannel = -1;
            s_CachedUVsVertexCount = 0;
            s_CachedTriangles = null;
            s_CachedTrianglesMeshId = 0;
            s_CachedTrianglesSubmesh = -999;
            s_CachedIslandOverlap = null;
            s_CachedIslandOverlapMeshId = 0;
            s_CachedIslandOverlapChannel = -1;
            s_CachedIslandOverlapSubmesh = -2;
            s_CachedIslandOverlapUVHash = 0;
            UVInspectorTool.InvalidateMaterialCache();
            if (s_SceneCache != null)
            {
                s_SceneCache.isValid = false;
            }
        }

        #endregion

        #region UV Hit Testing & Islands

        public class DisjointSet
        {
            private readonly int[] m_Parent;

            public DisjointSet(int count)
            {
                m_Parent = new int[count];
                for (int i = 0; i < count; i++) m_Parent[i] = i;
            }

            public int Find(int i)
            {
                while (i != m_Parent[i])
                {
                    m_Parent[i] = m_Parent[m_Parent[i]];
                    i = m_Parent[i];
                }
                return i;
            }

            public void Union(int a, int b)
            {
                int rootA = Find(a);
                int rootB = Find(b);
                if (rootA != rootB) m_Parent[rootA] = rootB;
            }
        }

        public static HashSet<int> GetIslandVertices(int seedVertex, Vector2[] uvs, int[] triangles, Vector3[] vertices = null)
        {
            HashSet<int> result = new HashSet<int>();
            if (uvs == null || uvs.Length == 0 || seedVertex < 0 || seedVertex >= uvs.Length || triangles == null)
                return result;

            DisjointSet ds = new DisjointSet(uvs.Length);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];
                if (i0 < uvs.Length && i1 < uvs.Length && i2 < uvs.Length)
                {
                    ds.Union(i0, i1);
                    ds.Union(i1, i2);
                }
            }

            // Union coincident UV vertices (split normals in FBX)
            Dictionary<long, int> uvMap = new Dictionary<long, int>();
            for (int i = 0; i < uvs.Length; i++)
            {
                long key = ((long)Mathf.RoundToInt(uvs[i].x * 10000f) << 32) | (uint)Mathf.RoundToInt(uvs[i].y * 10000f);
                if (uvMap.TryGetValue(key, out int existing))
                {
                    if (vertices == null || i >= vertices.Length || existing >= vertices.Length || (vertices[i] - vertices[existing]).sqrMagnitude < 1e-5f)
                    {
                        ds.Union(i, existing);
                    }
                }
                else
                {
                    uvMap[key] = i;
                }
            }

            int targetRoot = ds.Find(seedVertex);
            for (int i = 0; i < uvs.Length; i++)
            {
                if (ds.Find(i) == targetRoot)
                {
                    result.Add(i);
                }
            }
            return result;
        }

        public static HashSet<int> GetConnectedVertices(
            int seedVertex,
            Vector3[] vertices,
            Vector2[] uvs,
            bool match3DPosition = true,
            bool matchUVPosition = true)
        {
            HashSet<int> result = new HashSet<int>();
            if (seedVertex < 0) return result;
            result.Add(seedVertex);

            // 1. Connect vertices sharing the exact same 3D position in the original FBX
            if (match3DPosition && vertices != null && seedVertex < vertices.Length)
            {
                Vector3 p = vertices[seedVertex];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 d = vertices[i] - p;
                    if (d.x * d.x + d.y * d.y + d.z * d.z < 1e-7f)
                    {
                        result.Add(i);
                    }
                }
            }

            // 2. Connect vertices sharing the exact same UV coordinates
            if (matchUVPosition && uvs != null && seedVertex < uvs.Length)
            {
                Vector2 uv = uvs[seedVertex];
                for (int i = 0; i < uvs.Length; i++)
                {
                    Vector2 d = uvs[i] - uv;
                    if (d.x * d.x + d.y * d.y < 1e-7f)
                    {
                        result.Add(i);
                    }
                }
            }

            return result;
        }

        public static HashSet<int> GetConnectedVertices(
            IEnumerable<int> seedVertices,
            Vector3[] vertices,
            Vector2[] uvs,
            bool match3DPosition = true,
            bool matchUVPosition = true)
        {
            HashSet<int> result = new HashSet<int>();
            if (seedVertices == null) return result;

            foreach (int seed in seedVertices)
            {
                if (seed < 0) continue;
                result.Add(seed);

                if (match3DPosition && vertices != null && seed < vertices.Length)
                {
                    Vector3 p = vertices[seed];
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 d = vertices[i] - p;
                        if (d.x * d.x + d.y * d.y + d.z * d.z < 1e-7f)
                        {
                            result.Add(i);
                        }
                    }
                }

                if (matchUVPosition && uvs != null && seed < uvs.Length)
                {
                    Vector2 uv = uvs[seed];
                    for (int i = 0; i < uvs.Length; i++)
                    {
                        Vector2 d = uvs[i] - uv;
                        if (d.x * d.x + d.y * d.y < 1e-7f)
                        {
                            result.Add(i);
                        }
                    }
                }
            }

            return result;
        }

        public static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);

            bool hasNeg = (d1 < -1e-6f) || (d2 < -1e-6f) || (d3 < -1e-6f);
            bool hasPos = (d1 > 1e-6f) || (d2 > 1e-6f) || (d3 > 1e-6f);

            return !(hasNeg && hasPos);
        }

        public static int FindClosestVertex(Vector2[] uvs, Vector2 mouseUV, float maxDistUV)
        {
            if (uvs == null || uvs.Length == 0) return -1;
            int closest = -1;
            float minDistSqr = maxDistUV * maxDistUV;

            for (int i = 0; i < uvs.Length; i++)
            {
                float sqrDist = (uvs[i] - mouseUV).sqrMagnitude;
                if (sqrDist < minDistSqr)
                {
                    minDistSqr = sqrDist;
                    closest = i;
                }
            }
            return closest;
        }

        public static int FindTriangleAt(Vector2[] uvs, int[] triangles, Vector2 mouseUV)
        {
            if (uvs == null || triangles == null) return -1;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                if (i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length) continue;

                Vector2 p0 = uvs[i0];
                Vector2 p1 = uvs[i1];
                Vector2 p2 = uvs[i2];

                float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x));
                float maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x));
                float minY = Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y));
                float maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y));

                if (mouseUV.x < minX || mouseUV.x > maxX || mouseUV.y < minY || mouseUV.y > maxY)
                    continue;

                if (PointInTriangle(mouseUV, p0, p1, p2))
                {
                    return i;
                }
            }
            return -1;
        }

        public static HashSet<int> GetQuadVertices(int triIdx, Vector2[] uvs, int[] triangles, Vector3[] vertices = null)
        {
            HashSet<int> result = new HashSet<int>();
            if (uvs == null || uvs.Length == 0 || triangles == null || triIdx < 0 || triIdx + 2 >= triangles.Length)
                return result;

            int i0 = triangles[triIdx];
            int i1 = triangles[triIdx + 1];
            int i2 = triangles[triIdx + 2];

            if (i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length)
                return result;

            result.Add(i0);
            result.Add(i1);
            result.Add(i2);

            Vector2 p0 = uvs[i0];
            Vector2 p1 = uvs[i1];
            Vector2 p2 = uvs[i2];

            int bestPartnerTri = -1;
            float bestScore = -float.MaxValue;

            int triNum = triIdx / 3;
            int siblingTriNum = (triNum % 2 == 0) ? (triNum + 1) : (triNum - 1);
            int siblingTriIdx = siblingTriNum * 3;

            // Search for candidate adjacent triangles that share an edge and form a convex quad
            for (int t = 0; t < triangles.Length; t += 3)
            {
                if (t == triIdx) continue;

                int j0 = triangles[t];
                int j1 = triangles[t + 1];
                int j2 = triangles[t + 2];

                if (j0 >= uvs.Length || j1 >= uvs.Length || j2 >= uvs.Length) continue;

                // Match vertices between T and candidate T'
                bool m0 = (j0 == i0 || j1 == i0 || j2 == i0 || (uvs[j0] - p0).sqrMagnitude < 1e-8f || (uvs[j1] - p0).sqrMagnitude < 1e-8f || (uvs[j2] - p0).sqrMagnitude < 1e-8f);
                bool m1 = (j0 == i1 || j1 == i1 || j2 == i1 || (uvs[j0] - p1).sqrMagnitude < 1e-8f || (uvs[j1] - p1).sqrMagnitude < 1e-8f || (uvs[j2] - p1).sqrMagnitude < 1e-8f);
                bool m2 = (j0 == i2 || j1 == i2 || j2 == i2 || (uvs[j0] - p2).sqrMagnitude < 1e-8f || (uvs[j1] - p2).sqrMagnitude < 1e-8f || (uvs[j2] - p2).sqrMagnitude < 1e-8f);

                int sharedCount = 0;
                int sharedA = -1, sharedB = -1;
                int nonSharedT = -1;

                if (m0) { sharedCount++; if (sharedA < 0) sharedA = i0; else sharedB = i0; } else { nonSharedT = i0; }
                if (m1) { sharedCount++; if (sharedA < 0) sharedA = i1; else sharedB = i1; } else { nonSharedT = i1; }
                if (m2) { sharedCount++; if (sharedA < 0) sharedA = i2; else sharedB = i2; } else { nonSharedT = i2; }

                if (sharedCount != 2 || nonSharedT < 0) continue;

                // Find non-shared vertex in candidate
                int nonSharedCand = -1;
                bool c0 = (j0 == sharedA || j0 == sharedB || (uvs[j0] - uvs[sharedA]).sqrMagnitude < 1e-8f || (uvs[j0] - uvs[sharedB]).sqrMagnitude < 1e-8f);
                bool c1 = (j1 == sharedA || j1 == sharedB || (uvs[j1] - uvs[sharedA]).sqrMagnitude < 1e-8f || (uvs[j1] - uvs[sharedB]).sqrMagnitude < 1e-8f);
                bool c2 = (j2 == sharedA || j2 == sharedB || (uvs[j2] - uvs[sharedA]).sqrMagnitude < 1e-8f || (uvs[j2] - uvs[sharedB]).sqrMagnitude < 1e-8f);

                if (!c0) nonSharedCand = j0;
                else if (!c1) nonSharedCand = j1;
                else if (!c2) nonSharedCand = j2;
                else continue;

                Vector2 uvSharedA = uvs[sharedA];
                Vector2 uvSharedB = uvs[sharedB];
                Vector2 uvNonSharedT = uvs[nonSharedT];
                Vector2 uvNonSharedCand = uvs[nonSharedCand];

                // Check convexity: non-shared vertices must lie on opposite sides of the shared edge
                float cross1 = (uvSharedB.x - uvSharedA.x) * (uvNonSharedT.y - uvSharedA.y) - (uvSharedB.y - uvSharedA.y) * (uvNonSharedT.x - uvSharedA.x);
                float cross2 = (uvSharedB.x - uvSharedA.x) * (uvNonSharedCand.y - uvSharedA.y) - (uvSharedB.y - uvSharedA.y) * (uvNonSharedCand.x - uvSharedA.x);

                if (cross1 * cross2 >= -1e-8f) continue; // Not opposite sides or degenerate

                // Check diagonal intersection: shared vertices must lie on opposite sides of line between non-shared vertices
                float cross3 = (uvNonSharedCand.x - uvNonSharedT.x) * (uvSharedA.y - uvNonSharedT.y) - (uvNonSharedCand.y - uvNonSharedT.y) * (uvSharedA.x - uvNonSharedT.x);
                float cross4 = (uvNonSharedCand.x - uvNonSharedT.x) * (uvSharedB.y - uvNonSharedT.y) - (uvNonSharedCand.y - uvNonSharedT.y) * (uvSharedB.x - uvNonSharedT.x);

                if (cross3 * cross4 >= -1e-8f) continue; // Diagonals do not cross internally

                float score = 0f;

                // Consecutive sibling bonus (standard quad triangulation)
                if (t == siblingTriIdx)
                {
                    score += 20f;
                }

                // 3D Coplanarity check
                float normDot = 1f;
                if (vertices != null && i0 < vertices.Length && i1 < vertices.Length && i2 < vertices.Length &&
                    j0 < vertices.Length && j1 < vertices.Length && j2 < vertices.Length)
                {
                    Vector3 n1 = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
                    Vector3 n2 = Vector3.Cross(vertices[j1] - vertices[j0], vertices[j2] - vertices[j0]);
                    if (n1.sqrMagnitude > 1e-8f && n2.sqrMagnitude > 1e-8f)
                    {
                        normDot = Vector3.Dot(n1.normalized, n2.normalized);
                        if (normDot < 0.2f) continue; // Normals differ by > 78 degrees
                        score += normDot * 5f;
                    }
                }

                // UV edge parallelism
                Vector2 e1 = uvSharedA - uvNonSharedT;
                Vector2 e2 = uvNonSharedCand - uvSharedB;
                Vector2 e3 = uvSharedB - uvNonSharedT;
                Vector2 e4 = uvNonSharedCand - uvSharedA;

                if (e1.sqrMagnitude > 1e-8f && e2.sqrMagnitude > 1e-8f)
                    score += Vector2.Dot(e1.normalized, e2.normalized);
                if (e3.sqrMagnitude > 1e-8f && e4.sqrMagnitude > 1e-8f)
                    score += Vector2.Dot(e3.normalized, e4.normalized);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPartnerTri = t;
                }
            }

            if (bestPartnerTri >= 0)
            {
                result.Add(triangles[bestPartnerTri]);
                result.Add(triangles[bestPartnerTri + 1]);
                result.Add(triangles[bestPartnerTri + 2]);
            }

            return result;
        }

        public static void ExecuteSingleClickSelection(
            Rect canvasRect,
            float minX, float maxX, float minY, float maxY,
            Vector2 mousePos,
            Vector2[] uvs,
            int[] triangles,
            HashSet<int> selectedVertices,
            UVSelectionMode selectionMode,
            bool isAdd,
            bool isRemove,
            Vector3[] vertices = null,
            bool selectConnectedFBX = true)
        {
            if (uvs == null || uvs.Length == 0 || selectedVertices == null) return;

            float clickU = minX + ((mousePos.x - canvasRect.x) / canvasRect.width) * (maxX - minX);
            float clickV = minY + (1f - (mousePos.y - canvasRect.y) / canvasRect.height) * (maxY - minY);
            Vector2 mouseUV = new Vector2(clickU, clickV);

            // Click threshold in UV units: ~12 pixels on screen
            float uvPickRadius = (12f / Mathf.Max(1f, canvasRect.height)) * (maxY - minY);

            HashSet<int> targetVertices = new HashSet<int>();

            if (selectionMode == UVSelectionMode.Vertex)
            {
                int closest = FindClosestVertex(uvs, mouseUV, uvPickRadius);
                if (closest >= 0)
                {
                    if (selectConnectedFBX)
                    {
                        targetVertices.UnionWith(GetConnectedVertices(closest, vertices, uvs));
                    }
                    else
                    {
                        targetVertices.Add(closest);
                    }
                }
            }
            else if (selectionMode == UVSelectionMode.Quad || selectionMode == UVSelectionMode.Face)
            {
                int triIdx = triangles != null ? FindTriangleAt(uvs, triangles, mouseUV) : -1;
                if (triIdx >= 0 && triIdx + 2 < triangles.Length)
                {
                    // In Quad mode, or Face mode without Alt held: select the whole connected quad
                    if (selectionMode == UVSelectionMode.Quad || !Event.current.alt)
                    {
                        targetVertices = GetQuadVertices(triIdx, uvs, triangles, vertices);
                    }
                    else
                    {
                        // Alt + Click in Face mode allows selecting a single triangle
                        targetVertices.Add(triangles[triIdx]);
                        targetVertices.Add(triangles[triIdx + 1]);
                        targetVertices.Add(triangles[triIdx + 2]);
                    }
                }
            }
            else // UVSelectionMode.Island
            {
                int seedVert = -1;
                int triIdx = triangles != null ? FindTriangleAt(uvs, triangles, mouseUV) : -1;
                if (triIdx >= 0 && triIdx < triangles.Length)
                {
                    seedVert = triangles[triIdx];
                }
                else
                {
                    seedVert = FindClosestVertex(uvs, mouseUV, uvPickRadius);
                }

                if (seedVert >= 0)
                {
                    targetVertices = GetIslandVertices(seedVert, uvs, triangles, vertices);
                }
            }

            if (targetVertices.Count > 0)
            {
                if (isAdd)
                {
                    selectedVertices.UnionWith(targetVertices);
                }
                else if (isRemove)
                {
                    selectedVertices.ExceptWith(targetVertices);
                }
                else
                {
                    selectedVertices.Clear();
                    selectedVertices.UnionWith(targetVertices);
                }
            }
            else
            {
                if (!isAdd && !isRemove)
                {
                    selectedVertices.Clear();
                }
            }
        }

        public static void ExecuteMarqueeSelection(
            Rect canvasRect,
            float minX, float maxX, float minY, float maxY,
            Vector2 startMouse, Vector2 currentMouse,
            Vector2[] uvs,
            int[] triangles,
            HashSet<int> selectedVertices,
            UVSelectionMode selectionMode,
            bool isAdd,
            bool isRemove,
            Vector3[] vertices = null,
            bool selectConnectedFBX = true)
        {
            if (uvs == null || uvs.Length == 0 || selectedVertices == null) return;

            Rect screenRect = Rect.MinMaxRect(
                Mathf.Min(startMouse.x, currentMouse.x),
                Mathf.Min(startMouse.y, currentMouse.y),
                Mathf.Max(startMouse.x, currentMouse.x),
                Mathf.Max(startMouse.y, currentMouse.y)
            );

            // Convert screen rect to UV bounding box
            float boxMinU = minX + ((screenRect.xMin - canvasRect.x) / canvasRect.width) * (maxX - minX);
            float boxMaxU = minX + ((screenRect.xMax - canvasRect.x) / canvasRect.width) * (maxX - minX);
            float boxMinV = minY + (1f - (screenRect.yMax - canvasRect.y) / canvasRect.height) * (maxY - minY);
            float boxMaxV = minY + (1f - (screenRect.yMin - canvasRect.y) / canvasRect.height) * (maxY - minY);

            HashSet<int> enclosedVertices = new HashSet<int>();
            for (int i = 0; i < uvs.Length; i++)
            {
                Vector2 uv = uvs[i];
                if (uv.x >= boxMinU && uv.x <= boxMaxU && uv.y >= boxMinV && uv.y <= boxMaxV)
                {
                    enclosedVertices.Add(i);
                }
            }

            HashSet<int> targetVertices = new HashSet<int>();

            if (selectionMode == UVSelectionMode.Vertex)
            {
                if (selectConnectedFBX)
                {
                    targetVertices.UnionWith(GetConnectedVertices(enclosedVertices, vertices, uvs));
                }
                else
                {
                    targetVertices = enclosedVertices;
                }
            }
            else if (selectionMode == UVSelectionMode.Quad || selectionMode == UVSelectionMode.Face)
            {
                if (triangles != null)
                {
                    HashSet<int> processedQuads = new HashSet<int>();
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int t0 = triangles[i];
                        int t1 = triangles[i + 1];
                        int t2 = triangles[i + 2];
                        if (enclosedVertices.Contains(t0) || enclosedVertices.Contains(t1) || enclosedVertices.Contains(t2))
                        {
                            if (selectionMode == UVSelectionMode.Quad)
                            {
                                if (!processedQuads.Contains(i))
                                {
                                    var quad = GetQuadVertices(i, uvs, triangles, vertices);
                                    targetVertices.UnionWith(quad);
                                    processedQuads.Add(i);
                                }
                            }
                            else
                            {
                                targetVertices.Add(t0);
                                targetVertices.Add(t1);
                                targetVertices.Add(t2);
                            }
                        }
                    }
                }
                else
                {
                    targetVertices = enclosedVertices;
                }
            }
            else // UVSelectionMode.Island
            {
                if (triangles != null && enclosedVertices.Count > 0)
                {
                    DisjointSet ds = new DisjointSet(uvs.Length);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int i0 = triangles[i];
                        int i1 = triangles[i + 1];
                        int i2 = triangles[i + 2];
                        if (i0 < uvs.Length && i1 < uvs.Length && i2 < uvs.Length)
                        {
                            ds.Union(i0, i1);
                            ds.Union(i1, i2);
                        }
                    }

                    HashSet<int> touchedRoots = new HashSet<int>();
                    foreach (int v in enclosedVertices)
                    {
                        touchedRoots.Add(ds.Find(v));
                    }

                    for (int i = 0; i < uvs.Length; i++)
                    {
                        if (touchedRoots.Contains(ds.Find(i)))
                        {
                            targetVertices.Add(i);
                        }
                    }
                }
                else
                {
                    targetVertices = enclosedVertices;
                }
            }

            if (isAdd)
            {
                selectedVertices.UnionWith(targetVertices);
            }
            else if (isRemove)
            {
                selectedVertices.ExceptWith(targetVertices);
            }
            else
            {
                selectedVertices.Clear();
                selectedVertices.UnionWith(targetVertices);
            }
        }

        public static bool IsPointOnSelection(Vector2 mouseUV, Vector2[] uvs, int[] triangles, HashSet<int> selectedVertices, float pickRadius)
        {
            if (uvs == null || selectedVertices == null || selectedVertices.Count == 0) return false;

            float pickRadiusSqr = pickRadius * pickRadius;
            foreach (int idx in selectedVertices)
            {
                if (idx >= 0 && idx < uvs.Length)
                {
                    if ((uvs[idx] - mouseUV).sqrMagnitude <= pickRadiusSqr) return true;
                }
            }

            if (triangles != null)
            {
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int t0 = triangles[i];
                    int t1 = triangles[i + 1];
                    int t2 = triangles[i + 2];

                    if (selectedVertices.Contains(t0) && selectedVertices.Contains(t1) && selectedVertices.Contains(t2))
                    {
                        if (t0 < uvs.Length && t1 < uvs.Length && t2 < uvs.Length)
                        {
                            if (PointInTriangle(mouseUV, uvs[t0], uvs[t1], uvs[t2]))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        #endregion

        #region UV Island Overlap & Shared Texture Detection

        public class UVIsland
        {
            public int id;
            public int rootIndex;
            public readonly List<int> vertexIndices = new List<int>();
            public readonly List<int> triangleIndices = new List<int>();
            public Rect bounds;
            public Vector2 centroid;
            public float area;
            public readonly List<int> overlappingIslandIds = new List<int>();
            public int sharedGroupId = -1;
        }

        public class IslandOverlapGroup
        {
            public int groupId;
            public readonly List<int> islandIds = new List<int>();
            public Vector2 centroid;
            public Rect bounds;
        }

        public class IslandOverlapResult
        {
            public readonly List<UVIsland> islands = new List<UVIsland>();
            public readonly List<IslandOverlapGroup> sharedGroups = new List<IslandOverlapGroup>();
            public int totalOverlappingIslands;
            public readonly Dictionary<int, int> vertexToIslandMap = new Dictionary<int, int>();
            public readonly HashSet<int> overlappingVertexIndices = new HashSet<int>();
            public readonly HashSet<int> overlappingTriangles = new HashSet<int>();
        }

        private static IslandOverlapResult s_CachedIslandOverlap;
        private static int s_CachedIslandOverlapMeshId;
        private static int s_CachedIslandOverlapChannel = -1;
        private static int s_CachedIslandOverlapSubmesh = -2;
        private static int s_CachedIslandOverlapUVHash;

        public static IslandOverlapResult GetIslandOverlapInfo(Mesh mesh, int channel, int submeshIndex, Vector2[] liveUVsOverride = null)
        {
            if (mesh == null) return null;

            int meshId = mesh.GetInstanceID();
            Vector2[] uvs = liveUVsOverride ?? GetUVs(mesh, channel);
            if (uvs == null || uvs.Length == 0) return null;

            int uvHash = ComputeUVHash(uvs);

            if (liveUVsOverride == null &&
                s_CachedIslandOverlap != null &&
                s_CachedIslandOverlapMeshId == meshId &&
                s_CachedIslandOverlapChannel == channel &&
                s_CachedIslandOverlapSubmesh == submeshIndex &&
                s_CachedIslandOverlapUVHash == uvHash)
            {
                return s_CachedIslandOverlap;
            }

            int[] triangles = GetTriangles(mesh, submeshIndex);
            IslandOverlapResult result = CalculateIslandOverlaps(uvs, triangles);

            if (liveUVsOverride == null)
            {
                s_CachedIslandOverlap = result;
                s_CachedIslandOverlapMeshId = meshId;
                s_CachedIslandOverlapChannel = channel;
                s_CachedIslandOverlapSubmesh = submeshIndex;
                s_CachedIslandOverlapUVHash = uvHash;
            }

            return result;
        }

        public static IslandOverlapResult CalculateIslandOverlaps(Vector2[] uvs, int[] triangles)
        {
            IslandOverlapResult result = new IslandOverlapResult();
            if (uvs == null || uvs.Length == 0 || triangles == null || triangles.Length == 0)
                return result;

            DisjointSet ds = new DisjointSet(uvs.Length);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];
                if (i0 < uvs.Length && i1 < uvs.Length && i2 < uvs.Length)
                {
                    ds.Union(i0, i1);
                    ds.Union(i1, i2);
                }
            }

            Dictionary<int, UVIsland> rootToIsland = new Dictionary<int, UVIsland>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];
                if (i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length) continue;

                int root = ds.Find(i0);
                if (!rootToIsland.TryGetValue(root, out UVIsland island))
                {
                    island = new UVIsland { id = rootToIsland.Count, rootIndex = root };
                    rootToIsland.Add(root, island);
                }
                island.triangleIndices.Add(i);
            }

            foreach (var kvp in rootToIsland)
            {
                UVIsland island = kvp.Value;
                HashSet<int> vertSet = new HashSet<int>();
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                Vector2 sumUV = Vector2.zero;
                float totalArea = 0f;

                for (int t = 0; t < island.triangleIndices.Count; t++)
                {
                    int triOffset = island.triangleIndices[t];
                    int i0 = triangles[triOffset];
                    int i1 = triangles[triOffset + 1];
                    int i2 = triangles[triOffset + 2];

                    vertSet.Add(i0);
                    vertSet.Add(i1);
                    vertSet.Add(i2);

                    Vector2 u0 = uvs[i0];
                    Vector2 u1 = uvs[i1];
                    Vector2 u2 = uvs[i2];

                    float triArea = Mathf.Abs((u1.x - u0.x) * (u2.y - u0.y) - (u2.x - u0.x) * (u1.y - u0.y)) * 0.5f;
                    totalArea += triArea;
                }

                island.vertexIndices.AddRange(vertSet);
                foreach (int v in vertSet)
                {
                    Vector2 uv = uvs[v];
                    if (uv.x < minX) minX = uv.x;
                    if (uv.y < minY) minY = uv.y;
                    if (uv.x > maxX) maxX = uv.x;
                    if (uv.y > maxY) maxY = uv.y;
                    sumUV += uv;
                    result.vertexToIslandMap[v] = island.id;
                }

                if (vertSet.Count > 0)
                {
                    island.centroid = sumUV / vertSet.Count;
                    island.bounds = new Rect(minX, minY, Mathf.Max(0.0001f, maxX - minX), Mathf.Max(0.0001f, maxY - minY));
                    island.area = totalArea;
                }

                result.islands.Add(island);
            }

            int islandCount = result.islands.Count;
            for (int i = 0; i < islandCount; i++)
            {
                UVIsland islandA = result.islands[i];
                for (int j = i + 1; j < islandCount; j++)
                {
                    UVIsland islandB = result.islands[j];

                    // 1. Broadphase AABB Overlap test
                    if (!islandA.bounds.Overlaps(islandB.bounds))
                        continue;

                    // 2. Narrowphase: test triangle pairs
                    bool islandsOverlap = false;
                    for (int ta = 0; ta < islandA.triangleIndices.Count && !islandsOverlap; ta++)
                    {
                        int offA = islandA.triangleIndices[ta];
                        Vector2 a0 = uvs[triangles[offA]];
                        Vector2 a1 = uvs[triangles[offA + 1]];
                        Vector2 a2 = uvs[triangles[offA + 2]];

                        float aMinX = Mathf.Min(a0.x, Mathf.Min(a1.x, a2.x));
                        float aMaxX = Mathf.Max(a0.x, Mathf.Max(a1.x, a2.x));
                        float aMinY = Mathf.Min(a0.y, Mathf.Min(a1.y, a2.y));
                        float aMaxY = Mathf.Max(a0.y, Mathf.Max(a1.y, a2.y));

                        for (int tb = 0; tb < islandB.triangleIndices.Count; tb++)
                        {
                            int offB = islandB.triangleIndices[tb];
                            Vector2 b0 = uvs[triangles[offB]];
                            Vector2 b1 = uvs[triangles[offB + 1]];
                            Vector2 b2 = uvs[triangles[offB + 2]];

                            float bMinX = Mathf.Min(b0.x, Mathf.Min(b1.x, b2.x));
                            float bMaxX = Mathf.Max(b0.x, Mathf.Max(b1.x, b2.x));
                            float bMinY = Mathf.Min(b0.y, Mathf.Min(b1.y, b2.y));
                            float bMaxY = Mathf.Max(b0.y, Mathf.Max(b1.y, b2.y));

                            if (aMaxX < bMinX - 1e-5f || aMinX > bMaxX + 1e-5f ||
                                aMaxY < bMinY - 1e-5f || aMinY > bMaxY + 1e-5f)
                                continue;

                            if (TrianglesOverlap2D(a0, a1, a2, b0, b1, b2))
                            {
                                islandsOverlap = true;
                                result.overlappingTriangles.Add(offA);
                                result.overlappingTriangles.Add(offB);
                                break;
                            }
                        }
                    }

                    if (islandsOverlap)
                    {
                        islandA.overlappingIslandIds.Add(islandB.id);
                        islandB.overlappingIslandIds.Add(islandA.id);
                    }
                }
            }

            DisjointSet overlapDS = new DisjointSet(islandCount);
            for (int i = 0; i < islandCount; i++)
            {
                UVIsland island = result.islands[i];
                if (island.overlappingIslandIds.Count > 0)
                {
                    result.totalOverlappingIslands++;
                    for (int k = 0; k < island.overlappingIslandIds.Count; k++)
                    {
                        int otherId = island.overlappingIslandIds[k];
                        overlapDS.Union(island.id, otherId);
                    }
                    for (int v = 0; v < island.vertexIndices.Count; v++)
                    {
                        result.overlappingVertexIndices.Add(island.vertexIndices[v]);
                    }
                }
            }

            Dictionary<int, IslandOverlapGroup> rootToGroup = new Dictionary<int, IslandOverlapGroup>();
            for (int i = 0; i < islandCount; i++)
            {
                UVIsland island = result.islands[i];
                if (island.overlappingIslandIds.Count > 0)
                {
                    int groupRoot = overlapDS.Find(island.id);
                    if (!rootToGroup.TryGetValue(groupRoot, out IslandOverlapGroup group))
                    {
                        group = new IslandOverlapGroup { groupId = rootToGroup.Count };
                        rootToGroup.Add(groupRoot, group);
                    }
                    group.islandIds.Add(island.id);
                    island.sharedGroupId = group.groupId;
                }
            }

            foreach (var kvp in rootToGroup)
            {
                IslandOverlapGroup group = kvp.Value;
                Vector2 groupCentroid = Vector2.zero;
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;

                for (int i = 0; i < group.islandIds.Count; i++)
                {
                    UVIsland isl = result.islands[group.islandIds[i]];
                    groupCentroid += isl.centroid;
                    if (isl.bounds.xMin < minX) minX = isl.bounds.xMin;
                    if (isl.bounds.yMin < minY) minY = isl.bounds.yMin;
                    if (isl.bounds.xMax > maxX) maxX = isl.bounds.xMax;
                    if (isl.bounds.yMax > maxY) maxY = isl.bounds.yMax;
                }

                group.centroid = groupCentroid / Mathf.Max(1, group.islandIds.Count);
                group.bounds = new Rect(minX, minY, Mathf.Max(0.0001f, maxX - minX), Mathf.Max(0.0001f, maxY - minY));
                result.sharedGroups.Add(group);
            }

            return result;
        }

        public static bool TrianglesOverlap2D(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 b0, Vector2 b1, Vector2 b2)
        {
            if (IsCoincidentTriangle(a0, a1, a2, b0, b1, b2))
                return true;

            if (SegmentsIntersectStrict(a0, a1, b0, b1) || SegmentsIntersectStrict(a0, a1, b1, b2) || SegmentsIntersectStrict(a0, a1, b2, b0) ||
                SegmentsIntersectStrict(a1, a2, b0, b1) || SegmentsIntersectStrict(a1, a2, b1, b2) || SegmentsIntersectStrict(a1, a2, b2, b0) ||
                SegmentsIntersectStrict(a2, a0, b0, b1) || SegmentsIntersectStrict(a2, a0, b1, b2) || SegmentsIntersectStrict(a2, a0, b2, b0))
            {
                return true;
            }

            if (PointInTriangleStrict(a0, b0, b1, b2) ||
                PointInTriangleStrict(a1, b0, b1, b2) ||
                PointInTriangleStrict(a2, b0, b1, b2))
            {
                return true;
            }

            if (PointInTriangleStrict(b0, a0, a1, a2) ||
                PointInTriangleStrict(b1, a0, a1, a2) ||
                PointInTriangleStrict(b2, a0, a1, a2))
            {
                return true;
            }

            Vector2 cA = (a0 + a1 + a2) / 3f;
            Vector2 cB = (b0 + b1 + b2) / 3f;
            if (PointInTriangleStrict(cA, b0, b1, b2) || PointInTriangleStrict(cB, a0, a1, a2))
            {
                return true;
            }

            return false;
        }

        public static bool IsCoincidentTriangle(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 b0, Vector2 b1, Vector2 b2, float eps = 0.001f)
        {
            bool a0Match = Vector2.Distance(a0, b0) < eps || Vector2.Distance(a0, b1) < eps || Vector2.Distance(a0, b2) < eps;
            if (!a0Match) return false;

            bool a1Match = Vector2.Distance(a1, b0) < eps || Vector2.Distance(a1, b1) < eps || Vector2.Distance(a1, b2) < eps;
            if (!a1Match) return false;

            bool a2Match = Vector2.Distance(a2, b0) < eps || Vector2.Distance(a2, b1) < eps || Vector2.Distance(a2, b2) < eps;
            return a2Match;
        }

        public static bool PointInTriangleStrict(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float eps = 1e-5f)
        {
            float d1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            float d2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
            float d3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);

            bool allPos = (d1 > eps) && (d2 > eps) && (d3 > eps);
            bool allNeg = (d1 < -eps) && (d2 < -eps) && (d3 < -eps);
            return allPos || allNeg;
        }

        public static bool SegmentsIntersectStrict(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float eps = 1e-5f)
        {
            float ccw1 = (p2.x - p1.x) * (p3.y - p1.y) - (p2.y - p1.y) * (p3.x - p1.x);
            float ccw2 = (p2.x - p1.x) * (p4.y - p1.y) - (p2.y - p1.y) * (p4.x - p1.x);
            float ccw3 = (p4.x - p3.x) * (p1.y - p3.y) - (p4.y - p3.y) * (p1.x - p3.x);
            float ccw4 = (p4.x - p3.x) * (p2.y - p3.y) - (p4.y - p3.y) * (p2.x - p3.x);

            bool p3p4Opposite = (ccw1 > eps && ccw2 < -eps) || (ccw1 < -eps && ccw2 > eps);
            bool p1p2Opposite = (ccw3 > eps && ccw4 < -eps) || (ccw3 < -eps && ccw4 > eps);

            return p3p4Opposite && p1p2Opposite;
        }

        #endregion

        #region Preview Geometry Cache

        private static int ComputeSelectionHash(HashSet<int> selected)
        {
            if (selected == null || selected.Count == 0) return 0;
            int hash = selected.Count;
            foreach (int val in selected)
            {
                hash = unchecked(hash * 397 ^ val);
            }
            return hash;
        }

        private static void DestroyBaseCachedMeshes()
        {
            s_Cached2DVertices = null;
            s_CachedLineIndices = null;
            s_CachedFillTris = null;
            s_CachedFlippedTris = null;
        }

        private static void DestroySelectedCachedMeshes()
        {
            s_CachedSelLineIndices = null;
            s_CachedSelOutlineIndices = null;
            s_CachedSelPointsIndices = null;
        }

        private static void DestroyCachedMeshes()
        {
            DestroyBaseCachedMeshes();
            DestroySelectedCachedMeshes();
        }

        private static void UpdateGeometryCache(Mesh mesh, int channel, int submeshIndex, Vector2[] currentUVs, HashSet<int> selectedVertices)
        {
            int uvHash = ComputeUVHash(currentUVs);
            int selHash = ComputeSelectionHash(selectedVertices);
            int meshId = mesh != null ? mesh.GetInstanceID() : 0;

            bool meshChanged = meshId != s_CachedMeshInstanceId ||
                               channel != s_CachedChannel ||
                               submeshIndex != s_CachedSubmesh ||
                               uvHash != s_CachedUVHash ||
                               s_CachedLineIndices == null ||
                               s_Cached2DVertices == null;

            bool selChanged = selHash != s_CachedSelectionHash;

            if (!meshChanged && !selChanged)
            {
                return;
            }

            s_CachedMeshInstanceId = meshId;
            s_CachedChannel = channel;
            s_CachedSubmesh = submeshIndex;
            s_CachedUVHash = uvHash;
            s_CachedSelectionHash = selHash;

            if (mesh == null || currentUVs == null || currentUVs.Length == 0)
            {
                DestroyCachedMeshes();
                return;
            }

            int[] triangles = GetTriangles(mesh, submeshIndex);
            if (triangles == null || triangles.Length == 0)
            {
                DestroyCachedMeshes();
                return;
            }

            Vector3[] vertices;
            if (meshChanged)
            {
                DestroyBaseCachedMeshes();

                if (s_Cached2DVertices == null || s_Cached2DVertices.Length != currentUVs.Length)
                {
                    vertices = new Vector3[currentUVs.Length];
                }
                else
                {
                    vertices = s_Cached2DVertices;
                }

                for (int i = 0; i < currentUVs.Length; i++)
                {
                    vertices[i].Set(currentUVs[i].x, currentUVs[i].y, 0f);
                }

                // 1. Line Indices (Wireframe)
                int triCount = triangles.Length / 3;
                int[] lineIndices = new int[triCount * 6];
                List<int> flippedTriangles = null;

                for (int i = 0; i < triCount; i++)
                {
                    int t0 = triangles[i * 3 + 0];
                    int t1 = triangles[i * 3 + 1];
                    int t2 = triangles[i * 3 + 2];

                    lineIndices[i * 6 + 0] = t0;
                    lineIndices[i * 6 + 1] = t1;
                    lineIndices[i * 6 + 2] = t1;
                    lineIndices[i * 6 + 3] = t2;
                    lineIndices[i * 6 + 4] = t2;
                    lineIndices[i * 6 + 5] = t0;

                    if (t0 < currentUVs.Length && t1 < currentUVs.Length && t2 < currentUVs.Length)
                    {
                        Vector2 p0 = currentUVs[t0];
                        Vector2 p1 = currentUVs[t1];
                        Vector2 p2 = currentUVs[t2];
                        float cross = (p1.x - p0.x) * (p2.y - p0.y) - (p1.y - p0.y) * (p2.x - p0.x);
                        if (cross < -1e-6f)
                        {
                            if (flippedTriangles == null) flippedTriangles = new List<int>();
                            flippedTriangles.Add(t0);
                            flippedTriangles.Add(t1);
                            flippedTriangles.Add(t2);
                        }
                    }
                }

                s_Cached2DVertices = vertices;
                s_CachedLineIndices = lineIndices;
                s_CachedFillTris = triangles;
                s_CachedFlippedTris = flippedTriangles != null && flippedTriangles.Count > 0 ? flippedTriangles.ToArray() : null;
            }
            else
            {
                vertices = s_Cached2DVertices;
            }

            // Always update Selection Caches
            DestroySelectedCachedMeshes();

            if (selectedVertices != null && selectedVertices.Count > 0)
            {
                // A. Selected Line Indices
                List<int> selLines = new List<int>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int t0 = triangles[i];
                    int t1 = triangles[i + 1];
                    int t2 = triangles[i + 2];

                    bool s0 = selectedVertices.Contains(t0);
                    bool s1 = selectedVertices.Contains(t1);
                    bool s2 = selectedVertices.Contains(t2);

                    if (s0 && s1) { selLines.Add(t0); selLines.Add(t1); }
                    if (s1 && s2) { selLines.Add(t1); selLines.Add(t2); }
                    if (s2 && s0) { selLines.Add(t2); selLines.Add(t0); }
                }

                // B. Selected Boundary Outline (edges appearing in exactly 1 selected triangle)
                List<int> selOutline = ExtractBoundaryOutlineEdges(triangles, selectedVertices);
                if (selOutline.Count == 0 && selLines.Count > 0)
                {
                    selOutline.AddRange(selLines);
                }

                // C. Selected Points
                int[] selPoints = new int[selectedVertices.Count];
                int pIdx = 0;
                foreach (int v in selectedVertices)
                {
                    if (v >= 0 && v < vertices.Length)
                    {
                        selPoints[pIdx++] = v;
                    }
                }

                if (pIdx > 0 && pIdx < selPoints.Length)
                {
                    Array.Resize(ref selPoints, pIdx);
                }

                s_CachedSelLineIndices = selLines.Count > 0 ? selLines.ToArray() : null;
                s_CachedSelOutlineIndices = selOutline.Count > 0 ? selOutline.ToArray() : null;
                s_CachedSelPointsIndices = pIdx > 0 ? selPoints : null;
            }
        }

        public static List<int> ExtractBoundaryOutlineEdges(int[] triangles, HashSet<int> selectedVertices)
        {
            List<int> selOutline = new List<int>();
            if (triangles == null || selectedVertices == null || selectedVertices.Count == 0)
                return selOutline;

            s_ScratchEdgeUsage.Clear();
            s_ScratchEdgeMap.Clear();

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int t0 = triangles[i];
                int t1 = triangles[i + 1];
                int t2 = triangles[i + 2];

                bool s0 = selectedVertices.Contains(t0);
                bool s1 = selectedVertices.Contains(t1);
                bool s2 = selectedVertices.Contains(t2);

                if (s0 && s1 && s2)
                {
                    RecordOutlineEdge(t0, t1, s_ScratchEdgeUsage, s_ScratchEdgeMap);
                    RecordOutlineEdge(t1, t2, s_ScratchEdgeUsage, s_ScratchEdgeMap);
                    RecordOutlineEdge(t2, t0, s_ScratchEdgeUsage, s_ScratchEdgeMap);
                }
            }

            foreach (var kvp in s_ScratchEdgeUsage)
            {
                if (kvp.Value == 1)
                {
                    var pair = s_ScratchEdgeMap[kvp.Key];
                    selOutline.Add(pair.Key);
                    selOutline.Add(pair.Value);
                }
            }

            return selOutline;
        }

        private static void RecordOutlineEdge(int a, int b, Dictionary<long, int> usage, Dictionary<long, KeyValuePair<int, int>> map)
        {
            long key = a < b ? (((long)a << 32) | (uint)b) : (((long)b << 32) | (uint)a);
            if (usage.TryGetValue(key, out int count))
            {
                usage[key] = count + 1;
            }
            else
            {
                usage[key] = 1;
                map[key] = new KeyValuePair<int, int>(a, b);
            }
        }

        #endregion

        #region Preview Drawing Engine

        public static void DrawPreviewGUI(
            Rect canvasRect,
            Mesh mesh,
            int channel,
            int submeshIndex,
            UVBackgroundMode bgMode,
            Texture2D customTex,
            Texture2D materialTex,
            float texAlpha,
            bool repeatTex,
            Color wireColor,
            Color faceFillColor,
            Color flippedFaceColor,
            bool showWireframe,
            bool showFaces,
            bool showVertices,
            bool showFlipped,
            bool showGrid,
            bool showUnitBounds,
            ref Vector2 viewCenter,
            ref float viewZoom,
            Vector2[] liveUVsOverride = null,
            HashSet<int> selectedVertices = null,
            UVSelectionMode selectionMode = UVSelectionMode.Island,
            CanvasTool activeTool = CanvasTool.Select,
            Action onSelectionChanged = null,
            Action<Vector2[]> onUVsCommitted = null,
            Action onRepaintRequest = null,
            bool showOverlap = true,
            Color overlapColor = default,
            bool selectConnectedFBX = true)
        {
            Event evt = Event.current;
            Vector2 mousePos = evt.mousePosition;
            bool isHovered = canvasRect.Contains(mousePos);

            // Calculate View Coordinates in UV Space
            float aspect = canvasRect.width / Mathf.Max(1f, canvasRect.height);
            float halfSpanY = 0.55f / Mathf.Max(0.01f, viewZoom);
            float halfSpanX = halfSpanY * aspect;

            float minX = viewCenter.x - halfSpanX;
            float maxX = viewCenter.x + halfSpanX;
            float minY = viewCenter.y - halfSpanY;
            float maxY = viewCenter.y + halfSpanY;

            // Mouse hover coordinates in UV space
            float hoverU = minX + ((mousePos.x - canvasRect.x) / canvasRect.width) * (maxX - minX);
            float hoverV = minY + (1f - (mousePos.y - canvasRect.y) / canvasRect.height) * (maxY - minY);
            Vector2 hoverUV = new Vector2(hoverU, hoverV);

            int controlID = GUIUtility.GetControlID(FocusType.Passive);

            Vector2[] uvs = liveUVsOverride ?? GetUVs(mesh, channel);
            int[] triangles = GetTriangles(mesh, submeshIndex);
            IslandOverlapResult overlapInfo = GetIslandOverlapInfo(mesh, channel, submeshIndex, liveUVsOverride);

            // Cursor Handling
            if (isHovered)
            {
                if (activeTool == CanvasTool.Pan || evt.alt || evt.button == 2)
                {
                    EditorGUIUtility.AddCursorRect(canvasRect, MouseCursor.Pan);
                }
                else if (activeTool == CanvasTool.Move)
                {
                    EditorGUIUtility.AddCursorRect(canvasRect, MouseCursor.MoveArrow);
                }
                else
                {
                    float uvPickRadius = (12f / Mathf.Max(1f, canvasRect.height)) * (maxY - minY);
                    bool onSelection = selectedVertices != null && selectedVertices.Count > 0 &&
                                       IsPointOnSelection(hoverUV, uvs, triangles, selectedVertices, uvPickRadius);
                    if (onSelection)
                    {
                        EditorGUIUtility.AddCursorRect(canvasRect, MouseCursor.MoveArrow);
                    }
                    else
                    {
                        EditorGUIUtility.AddCursorRect(canvasRect, MouseCursor.Arrow);
                    }
                }
            }

            // Cancel Drag with Escape
            if (s_IsDraggingUVs && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                s_IsDraggingUVs = false;
                s_DragOriginalUVs = null;
                s_DragCurrentDeltaUV = Vector2.zero;
                s_DragOriginalCentroid = Vector2.zero;
                s_HasUVMoved = false;
                if (GUIUtility.hotControl == controlID) GUIUtility.hotControl = 0;
                evt.Use();
                GUI.changed = true;
                if (onRepaintRequest != null)
                    onRepaintRequest();
                else
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }

            // Handle Undo / Redo shortcuts (Ctrl+Z / Ctrl+Y) when hovering over UV canvas
            if (!s_IsDraggingUVs && HandleUndoRedoEvents(evt, isHovered, mesh, channel, selectedVertices, () =>
            {
                InvalidateCache();
                onSelectionChanged?.Invoke();
                onRepaintRequest?.Invoke();
                SceneView.RepaintAll();
            }))
            {
                return;
            }

            // Hotkey 'L': Select Connected UV Island (Blender-style Select Linked)
            if (!EditorGUIUtility.editingTextField && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.L)
            {
                int seedVert = -1;
                float uvPickRadius = (14f / Mathf.Max(1f, canvasRect.height)) * (maxY - minY);

                // If hovering over canvas, try to find element under cursor
                if (isHovered && triangles != null && uvs != null && uvs.Length > 0)
                {
                    int triIdx = FindTriangleAt(uvs, triangles, hoverUV);
                    if (triIdx >= 0 && triIdx < triangles.Length)
                    {
                        seedVert = triangles[triIdx];
                    }
                    else
                    {
                        seedVert = FindClosestVertex(uvs, hoverUV, uvPickRadius);
                    }
                }

                if (seedVert >= 0)
                {
                    HashSet<int> island = GetIslandVertices(seedVert, uvs, triangles, GetCachedMeshVertices(mesh));
                    if (island.Count > 0)
                    {
                        if (evt.shift)
                        {
                            selectedVertices.UnionWith(island);
                        }
                        else if (evt.control || evt.alt)
                        {
                            selectedVertices.ExceptWith(island);
                        }
                        else
                        {
                            selectedVertices.Clear();
                            selectedVertices.UnionWith(island);
                        }
                        InvalidateCache();
                        onSelectionChanged?.Invoke();
                        evt.Use();
                        GUI.changed = true;
                    }
                }
                else if (selectedVertices != null && selectedVertices.Count > 0 && triangles != null && uvs != null && (isHovered || GUIUtility.hotControl == 0))
                {
                    // No element under cursor: expand all currently selected vertices to their complete connected UV islands
                    DisjointSet ds = new DisjointSet(uvs.Length);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int i0 = triangles[i];
                        int i1 = triangles[i + 1];
                        int i2 = triangles[i + 2];
                        if (i0 < uvs.Length && i1 < uvs.Length && i2 < uvs.Length)
                        {
                            ds.Union(i0, i1);
                            ds.Union(i1, i2);
                        }
                    }

                    Vector3[] meshVerts = GetCachedMeshVertices(mesh);
                    Dictionary<long, int> uvMap = new Dictionary<long, int>();
                    for (int i = 0; i < uvs.Length; i++)
                    {
                        long key = ((long)Mathf.RoundToInt(uvs[i].x * 10000f) << 32) | (uint)Mathf.RoundToInt(uvs[i].y * 10000f);
                        if (uvMap.TryGetValue(key, out int existing))
                        {
                            if (meshVerts == null || i >= meshVerts.Length || existing >= meshVerts.Length || (meshVerts[i] - meshVerts[existing]).sqrMagnitude < 1e-5f)
                            {
                                ds.Union(i, existing);
                            }
                        }
                        else
                        {
                            uvMap[key] = i;
                        }
                    }

                    HashSet<int> roots = new HashSet<int>();
                    foreach (int v in selectedVertices)
                    {
                        if (v >= 0 && v < uvs.Length) roots.Add(ds.Find(v));
                    }

                    for (int i = 0; i < uvs.Length; i++)
                    {
                        if (roots.Contains(ds.Find(i)))
                        {
                            selectedVertices.Add(i);
                        }
                    }

                    InvalidateCache();
                    onSelectionChanged?.Invoke();
                    evt.Use();
                    GUI.changed = true;
                }
            }

            // Prevent editor context menu inside interactive UV canvas
            if (isHovered && (evt.type == EventType.ContextClick || (evt.type == EventType.MouseUp && evt.button == 1)))
            {
                evt.Use();
            }

            // 1. Zoom (ScrollWheel)
            if (isHovered && evt.type == EventType.ScrollWheel)
            {
                float scroll = evt.delta.y;
                float zoomMult = scroll > 0 ? 0.88f : 1.13f;

                // Zoom centered around mouse cursor
                viewCenter = hoverUV + (viewCenter - hoverUV) / zoomMult;
                viewZoom = Mathf.Clamp(viewZoom * zoomMult, 0.05f, 100f);

                evt.Use();
                GUI.changed = true;
            }
            // 2. MouseDown
            else if (evt.type == EventType.MouseDown && isHovered)
            {
                // Right Click (button 1): Unselect the UV that has been selected (or cancel active drag)
                if (evt.button == 1)
                {
                    if (s_IsDraggingUVs)
                    {
                        s_IsDraggingUVs = false;
                        s_DragOriginalUVs = null;
                        s_DragCurrentDeltaUV = Vector2.zero;
                        s_DragOriginalCentroid = Vector2.zero;
                        s_HasUVMoved = false;
                        if (GUIUtility.hotControl == controlID) GUIUtility.hotControl = 0;
                    }
                    if (s_IsMarqueeSelecting)
                    {
                        s_IsMarqueeSelecting = false;
                        if (GUIUtility.hotControl == controlID) GUIUtility.hotControl = 0;
                    }

                    if (selectedVertices != null && selectedVertices.Count > 0)
                    {
                        // If Shift or Ctrl is held and hovering over a specific selected element, unselect that specific element
                        if ((evt.shift || evt.control) && triangles != null && uvs != null && uvs.Length > 0)
                        {
                            ExecuteSingleClickSelection(
                                canvasRect, minX, maxX, minY, maxY,
                                mousePos, uvs, triangles,
                                selectedVertices, selectionMode,
                                isAdd: false, isRemove: true,
                                vertices: GetCachedMeshVertices(mesh),
                                selectConnectedFBX: selectConnectedFBX
                            );
                        }
                        else
                        {
                            // Standard Right Click: Unselect the UV that has been selected
                            selectedVertices.Clear();
                        }

                        InvalidateCache();
                        onSelectionChanged?.Invoke();
                        SceneView.RepaintAll();
                        if (onRepaintRequest != null)
                            onRepaintRequest();
                    }

                    evt.Use();
                    GUI.changed = true;
                }
                else if (evt.button == 2 || (evt.button == 0 && (evt.alt || activeTool == CanvasTool.Pan)))
                {
                    s_IsPanning = true;
                    s_PanStartMouse = mousePos;
                    s_PanStartCenter = viewCenter;
                    GUIUtility.hotControl = controlID;
                    evt.Use();
                }
                else if (evt.button == 0)
                {
                    float uvPickRadius = (12f / Mathf.Max(1f, canvasRect.height)) * (maxY - minY);
                    bool onSelection = selectedVertices != null && selectedVertices.Count > 0 &&
                                       IsPointOnSelection(hoverUV, uvs, triangles, selectedVertices, uvPickRadius);

                    if (activeTool == CanvasTool.Move || (activeTool == CanvasTool.Select && onSelection && !evt.shift && !evt.control && !evt.alt))
                    {
                        // In Move mode, if nothing was selected yet, select what is under cursor first
                        if (activeTool == CanvasTool.Move && (selectedVertices == null || selectedVertices.Count == 0))
                        {
                            ExecuteSingleClickSelection(canvasRect, minX, maxX, minY, maxY, mousePos, uvs, triangles, selectedVertices, selectionMode, false, false, GetCachedMeshVertices(mesh), selectConnectedFBX);
                            onSelectionChanged?.Invoke();
                        }

                        if (selectedVertices != null && selectedVertices.Count > 0)
                        {
                            s_IsDraggingUVs = true;
                            s_DragUVStartMouse = mousePos;
                            s_DragOriginalUVs = (Vector2[])uvs.Clone();
                            s_DragCurrentDeltaUV = Vector2.zero;
                            s_HasUVMoved = false;

                            Vector2 sum = Vector2.zero;
                            int count = 0;
                            foreach (int v in selectedVertices)
                            {
                                if (v >= 0 && v < uvs.Length)
                                {
                                    sum += uvs[v];
                                    count++;
                                }
                            }
                            s_DragOriginalCentroid = count > 0 ? (sum / count) : hoverUV;

                            GUIUtility.hotControl = controlID;
                            evt.Use();
                        }
                    }
                    else if (activeTool == CanvasTool.Select)
                    {
                        s_IsMarqueeSelecting = true;
                        s_MarqueeStartMouse = mousePos;
                        s_MarqueeCurrentMouse = mousePos;
                        s_HasDragMoved = false;
                        GUIUtility.hotControl = controlID;
                        evt.Use();
                    }
                }
            }
            // 3. MouseDrag
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlID)
            {
                if (s_IsPanning)
                {
                    Vector2 mouseDelta = mousePos - s_PanStartMouse;
                    float spanX = maxX - minX;
                    float spanY = maxY - minY;

                    viewCenter = s_PanStartCenter - new Vector2(
                        (mouseDelta.x / canvasRect.width) * spanX,
                        -(mouseDelta.y / canvasRect.height) * spanY
                    );

                    evt.Use();
                    GUI.changed = true;
                    if (onRepaintRequest != null)
                        onRepaintRequest();
                    else
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
                else if (s_IsDraggingUVs)
                {
                    float spanX = maxX - minX;
                    float spanY = maxY - minY;

                    Vector2 mouseDelta = mousePos - s_DragUVStartMouse;
                    s_DragCurrentDeltaUV = new Vector2(
                        (mouseDelta.x / canvasRect.width) * spanX,
                        -(mouseDelta.y / canvasRect.height) * spanY
                    );

                    if (!s_HasUVMoved && mouseDelta.sqrMagnitude > 4f)
                    {
                        s_HasUVMoved = true;
                    }

                    evt.Use();
                    GUI.changed = true;
                    if (onRepaintRequest != null)
                        onRepaintRequest();
                    else
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
                else if (s_IsMarqueeSelecting)
                {
                    s_MarqueeCurrentMouse = mousePos;
                    if (!s_HasDragMoved && (mousePos - s_MarqueeStartMouse).sqrMagnitude > 16f)
                    {
                        s_HasDragMoved = true;
                    }
                    evt.Use();
                    GUI.changed = true;
                    if (onRepaintRequest != null)
                        onRepaintRequest();
                }
            }
            // 4. MouseUp
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlID)
            {
                if (s_IsPanning)
                {
                    s_IsPanning = false;
                    GUIUtility.hotControl = 0;
                    evt.Use();
                }
                else if (s_IsDraggingUVs)
                {
                    s_IsDraggingUVs = false;
                    GUIUtility.hotControl = 0;

                    if (s_HasUVMoved && s_DragOriginalUVs != null && selectedVertices != null && selectedVertices.Count > 0)
                    {
                        Vector2[] movedUVs = (Vector2[])s_DragOriginalUVs.Clone();
                        foreach (int idx in selectedVertices)
                        {
                            if (idx >= 0 && idx < movedUVs.Length)
                            {
                                movedUVs[idx] += s_DragCurrentDeltaUV;
                            }
                        }
                        onUVsCommitted?.Invoke(movedUVs);
                        InvalidateCache();
                        SceneView.RepaintAll();
                    }

                    s_DragOriginalUVs = null;
                    s_DragCurrentDeltaUV = Vector2.zero;
                    s_DragOriginalCentroid = Vector2.zero;
                    s_HasUVMoved = false;
                    evt.Use();
                    GUI.changed = true;
                    if (onRepaintRequest != null)
                        onRepaintRequest();
                    else
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
                else if (s_IsMarqueeSelecting)
                {
                    s_IsMarqueeSelecting = false;
                    GUIUtility.hotControl = 0;

                    if (selectedVertices != null && uvs != null && uvs.Length > 0)
                    {
                        bool isAdd = evt.shift;
                        bool isRemove = evt.control || evt.alt;

                        if (s_HasDragMoved)
                        {
                            ExecuteMarqueeSelection(
                                canvasRect, minX, maxX, minY, maxY,
                                s_MarqueeStartMouse, mousePos,
                                uvs, triangles,
                                selectedVertices, selectionMode,
                                isAdd, isRemove,
                                GetCachedMeshVertices(mesh),
                                selectConnectedFBX
                            );
                        }
                        else
                        {
                            ExecuteSingleClickSelection(
                                canvasRect, minX, maxX, minY, maxY,
                                mousePos,
                                uvs, triangles,
                                selectedVertices, selectionMode,
                                isAdd, isRemove,
                                GetCachedMeshVertices(mesh),
                                selectConnectedFBX
                            );
                        }

                        onSelectionChanged?.Invoke();
                        InvalidateCache();
                        SceneView.RepaintAll();
                    }

                    evt.Use();
                    GUI.changed = true;
                    if (onRepaintRequest != null)
                        onRepaintRequest();
                }
            }

            // Repaint the Canvas
            if (evt.type == EventType.Repaint)
            {
                int rtWidth = Mathf.RoundToInt(canvasRect.width);
                int rtHeight = Mathf.RoundToInt(canvasRect.height);
                EnsureRenderTexture(rtWidth, rtHeight);

                // Keep base cached geometry stable and non-allocating during drag
                UpdateGeometryCache(mesh, channel, submeshIndex, uvs, selectedVertices);

                RenderTexture prevRT = RenderTexture.active;
                RenderTexture.active = s_PreviewRT;

                Color bgColor = bgMode == UVBackgroundMode.LightGrid
                    ? new Color(0.82f, 0.82f, 0.82f, 1f)
                    : new Color(0.12f, 0.12f, 0.13f, 1f);

                GL.Clear(true, true, bgColor);

                Matrix4x4 proj = Matrix4x4.Ortho(minX, maxX, minY, maxY, -1f, 1f);
                GL.PushMatrix();
                GL.LoadProjectionMatrix(proj);
                GL.modelview = Matrix4x4.identity;

                Material mat = GetPreviewMaterial();
                if (mat != null)
                {
                    // 1. Draw Background (Texture or Checkerboard)
                    Texture2D bgTex = null;
                    if (bgMode == UVBackgroundMode.MaterialTexture) bgTex = materialTex;
                    else if (bgMode == UVBackgroundMode.CustomTexture) bgTex = customTex;
                    else if (bgMode == UVBackgroundMode.Checkerboard) bgTex = GetCheckerTexture();

                    if (bgTex != null && texAlpha > 0.001f)
                    {
                        mat.SetTexture("_MainTex", bgTex);
                        mat.SetColor("_Color", new Color(1f, 1f, 1f, texAlpha));
                        mat.SetPass(1); // Textured Pass

                        if (repeatTex || bgMode == UVBackgroundMode.Checkerboard)
                        {
                            // Draw repeating tiles across visible area
                            int startTileX = Mathf.FloorToInt(minX);
                            int endTileX = Mathf.CeilToInt(maxX);
                            int startTileY = Mathf.FloorToInt(minY);
                            int endTileY = Mathf.CeilToInt(maxY);

                            GL.Begin(GL.QUADS);
                            for (int tx = startTileX; tx < endTileX; tx++)
                            {
                                for (int ty = startTileY; ty < endTileY; ty++)
                                {
                                    GL.TexCoord2(0, 0); GL.Vertex3(tx, ty, 0);
                                    GL.TexCoord2(1, 0); GL.Vertex3(tx + 1, ty, 0);
                                    GL.TexCoord2(1, 1); GL.Vertex3(tx + 1, ty + 1, 0);
                                    GL.TexCoord2(0, 1); GL.Vertex3(tx, ty + 1, 0);
                                }
                            }
                            GL.End();
                        }
                        else
                        {
                            // Standard [0..1] quad
                            GL.Begin(GL.QUADS);
                            GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
                            GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
                            GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
                            GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
                            GL.End();
                        }
                    }

                    // 2. Draw Grid Lines
                    if (showGrid)
                    {
                        mat.SetPass(0); // Colored Pass
                        DrawGridLines(minX, maxX, minY, maxY, bgMode == UVBackgroundMode.LightGrid);
                    }

                    // 3. Draw [0..1] Unit Square Bounds
                    if (showUnitBounds)
                    {
                        mat.SetPass(0);
                        DrawUnitSquareBounds();
                    }

                    // 4. Draw Face Fill (Only if enabled and alpha > 0)
                    if (showFaces && faceFillColor.a > 0.001f && s_CachedFillTris != null && s_Cached2DVertices != null)
                    {
                        mat.SetPass(0);
                        GL.Begin(GL.TRIANGLES);
                        GL.Color(faceFillColor);
                        for (int i = 0; i < s_CachedFillTris.Length; i++)
                        {
                            GL.Vertex(s_Cached2DVertices[s_CachedFillTris[i]]);
                        }
                        GL.End();
                    }

                    // 5. Draw Flipped Triangles (Soft Blue Highlight)
                    if (showFlipped && s_CachedFlippedTris != null && s_Cached2DVertices != null)
                    {
                        mat.SetPass(0);
                        GL.Begin(GL.TRIANGLES);
                        GL.Color(flippedFaceColor);
                        for (int i = 0; i < s_CachedFlippedTris.Length; i++)
                        {
                            GL.Vertex(s_Cached2DVertices[s_CachedFlippedTris[i]]);
                        }
                        GL.End();
                    }

                    // 5.5. Draw Overlapping UV Islands (Wireframe Accent only, NO inside color)
                    if (showOverlap && overlapInfo != null && overlapInfo.totalOverlappingIslands > 0 && s_Cached2DVertices != null && triangles != null)
                    {
                        mat.SetPass(0);
                        Color lineCol = (overlapColor.a > 0.01f)
                            ? new Color(overlapColor.r, overlapColor.g, overlapColor.b, 0.95f)
                            : new Color(1.0f, 0.55f, 0.15f, 0.95f);

                        // Overlapping islands wireframe accent only - NO inside fill
                        GL.Begin(GL.LINES);
                        GL.Color(lineCol);
                        for (int i = 0; i < overlapInfo.islands.Count; i++)
                        {
                            var isl = overlapInfo.islands[i];
                            if (isl.overlappingIslandIds.Count == 0) continue;
                            for (int t = 0; t < isl.triangleIndices.Count; t++)
                            {
                                int off = isl.triangleIndices[t];
                                if (off + 2 < triangles.Length)
                                {
                                    Vector3 v0 = s_Cached2DVertices[triangles[off]];
                                    Vector3 v1 = s_Cached2DVertices[triangles[off + 1]];
                                    Vector3 v2 = s_Cached2DVertices[triangles[off + 2]];

                                    GL.Vertex(v0); GL.Vertex(v1);
                                    GL.Vertex(v1); GL.Vertex(v2);
                                    GL.Vertex(v2); GL.Vertex(v0);
                                }
                            }
                        }
                        GL.End();
                    }

                    // 6. Draw Wireframe Lines
                    if (showWireframe && s_CachedLineIndices != null && s_Cached2DVertices != null)
                    {
                        mat.SetPass(0);
                        GL.Begin(GL.LINES);
                        GL.Color(wireColor);
                        for (int i = 0; i < s_CachedLineIndices.Length; i++)
                        {
                            GL.Vertex(s_Cached2DVertices[s_CachedLineIndices[i]]);
                        }
                        GL.End();
                    }

                    // 6.5. Anchor Ghost at Original Position (while dragging - wireframe & outline only, NO inside fill)
                    if (s_IsDraggingUVs && s_HasUVMoved && selectedVertices != null && selectedVertices.Count > 0 && s_Cached2DVertices != null)
                    {
                        mat.SetPass(0);

                        // A. Anchor Ghost Wireframe at start position
                        if (s_CachedSelLineIndices != null)
                        {
                            GL.Begin(GL.LINES);
                            GL.Color(new Color(0.45f, 0.70f, 0.90f, 0.25f)); // Faint wireframe
                            for (int i = 0; i < s_CachedSelLineIndices.Length; i++)
                            {
                                GL.Vertex(s_Cached2DVertices[s_CachedSelLineIndices[i]]);
                            }
                            GL.End();
                        }

                        // C. Anchor Ghost Outline at start position
                        if (s_CachedSelOutlineIndices != null)
                        {
                            GL.Begin(GL.LINES);
                            GL.Color(new Color(0.40f, 0.75f, 1.0f, 0.45f)); // Soft cyan transparent outline
                            for (int i = 0; i < s_CachedSelOutlineIndices.Length; i++)
                            {
                                GL.Vertex(s_Cached2DVertices[s_CachedSelOutlineIndices[i]]);
                            }
                            GL.End();
                        }

                        // D. Anchor Displacement Guide Line (Connecting Original Centroid to Ghost Centroid)
                        Vector2 origCentroid = s_DragOriginalCentroid;
                        Vector2 ghostCentroid = origCentroid + s_DragCurrentDeltaUV;

                        GL.Begin(GL.LINES);
                        GL.Color(new Color(0.20f, 0.85f, 1.0f, 0.65f)); // Cyan leader line
                        GL.Vertex3(origCentroid.x, origCentroid.y, 0);
                        GL.Vertex3(ghostCentroid.x, ghostCentroid.y, 0);

                        // Anchor Crosshair at origin
                        float markerR = halfSpanY * 0.015f;
                        GL.Color(new Color(0.35f, 0.90f, 1.0f, 0.80f));
                        GL.Vertex3(origCentroid.x - markerR, origCentroid.y, 0);
                        GL.Vertex3(origCentroid.x + markerR, origCentroid.y, 0);
                        GL.Vertex3(origCentroid.x, origCentroid.y - markerR, 0);
                        GL.Vertex3(origCentroid.x, origCentroid.y + markerR, 0);

                        // Target Crosshair at ghost
                        GL.Color(new Color(0.25f, 0.95f, 1.0f, 0.95f));
                        GL.Vertex3(ghostCentroid.x - markerR, ghostCentroid.y, 0);
                        GL.Vertex3(ghostCentroid.x + markerR, ghostCentroid.y, 0);
                        GL.Vertex3(ghostCentroid.x, ghostCentroid.y - markerR, 0);
                        GL.Vertex3(ghostCentroid.x, ghostCentroid.y + markerR, 0);
                        GL.End();
                    }

                    // 7. Base Vertices (Points)
                    if (showVertices && s_Cached2DVertices != null && s_Cached2DVertices.Length < 5000)
                    {
                        mat.SetPass(0);
                        DrawVertexMarkers(s_Cached2DVertices, halfSpanY * 0.015f, minX, maxX, minY, maxY);
                    }

                    // 8. Render Selected Elements: Moving Ghost Preview vs Normal Selected Elements
                    if (s_IsDraggingUVs && s_HasUVMoved && selectedVertices != null && selectedVertices.Count > 0 && s_Cached2DVertices != null)
                    {
                        // -------------------------------------------------------------
                        // GHOST PREVIEW (Follows mouse drag smoothly in real-time, NO inside fill)
                        // -------------------------------------------------------------
                        GL.PushMatrix();
                        GL.MultMatrix(Matrix4x4.Translate(new Vector3(s_DragCurrentDeltaUV.x, s_DragCurrentDeltaUV.y, 0f)));

                        // A. Ghost Interior Wireframe
                        if (s_CachedSelLineIndices != null)
                        {
                            mat.SetPass(0);
                            GL.Begin(GL.LINES);
                            GL.Color(new Color(0.35f, 0.90f, 1.0f, 0.60f)); // Semi-transparent cyan lines
                            for (int i = 0; i < s_CachedSelLineIndices.Length; i++)
                            {
                                GL.Vertex(s_Cached2DVertices[s_CachedSelLineIndices[i]]);
                            }
                            GL.End();
                        }

                        // B. Ghost Transparent Boundary Outline (Dual-pass silhouette)
                        if (s_CachedSelOutlineIndices != null)
                        {
                            mat.SetPass(0);
                            float pixelU = (maxX - minX) / Mathf.Max(1f, canvasRect.width);
                            float pixelV = (maxY - minY) / Mathf.Max(1f, canvasRect.height);

                            // Pass 1: Translucent dark backing shadow for maximum contrast
                            GL.Begin(GL.LINES);
                            GL.Color(new Color(0.02f, 0.04f, 0.08f, 0.55f));
                            for (int i = 0; i < s_CachedSelOutlineIndices.Length; i += 2)
                            {
                                Vector3 pA = s_Cached2DVertices[s_CachedSelOutlineIndices[i]];
                                Vector3 pB = s_Cached2DVertices[s_CachedSelOutlineIndices[i + 1]];
                                GL.Vertex3(pA.x + pixelU, pA.y - pixelV, 0);
                                GL.Vertex3(pB.x + pixelU, pB.y - pixelV, 0);
                            }
                            GL.End();

                            // Pass 2: High-contrast electric cyan outer silhouette stroke
                            GL.Begin(GL.LINES);
                            GL.Color(new Color(0.25f, 0.95f, 1.0f, 0.95f));
                            for (int i = 0; i < s_CachedSelOutlineIndices.Length; i++)
                            {
                                GL.Vertex(s_Cached2DVertices[s_CachedSelOutlineIndices[i]]);
                            }
                            GL.End();
                        }

                        // C. Ghost Selected Vertex Markers (Glowing Cyan Stars)
                        if (s_CachedSelPointsIndices != null)
                        {
                            mat.SetPass(0);
                            DrawSelectedVertexMarkers(s_Cached2DVertices, s_CachedSelPointsIndices, halfSpanY * 0.018f, new Color(0.35f, 0.95f, 1.0f, 0.95f), minX, maxX, minY, maxY);
                        }

                        GL.PopMatrix();
                    }
                    else if (s_Cached2DVertices != null)
                    {
                        // -------------------------------------------------------------
                        // NORMAL (STATIC) SELECTION RENDERING (NO inside fill)
                        // -------------------------------------------------------------
                        // A. Draw Selected Wireframe (Internal Lines - bright gold)
                        if (s_CachedSelLineIndices != null)
                        {
                            mat.SetPass(0);
                            GL.Begin(GL.LINES);
                            GL.Color(new Color(1.0f, 0.88f, 0.25f, 0.95f));
                            for (int i = 0; i < s_CachedSelLineIndices.Length; i++)
                            {
                                GL.Vertex(s_Cached2DVertices[s_CachedSelLineIndices[i]]);
                            }
                            GL.End();
                        }

                        // B. Draw Crisp Outer Border (Dual-pass silhouette for maximum visibility with hollow/no inside color)
                        if (s_CachedSelOutlineIndices != null)
                        {
                            mat.SetPass(0);
                            float pixelU = (maxX - minX) / Mathf.Max(1f, canvasRect.width);
                            float pixelV = (maxY - minY) / Mathf.Max(1f, canvasRect.height);

                            // Pass 1: Backing drop-shadow stroke for high contrast on bright textures
                            GL.Begin(GL.LINES);
                            GL.Color(new Color(0.02f, 0.04f, 0.08f, 0.65f));
                            for (int i = 0; i < s_CachedSelOutlineIndices.Length; i += 2)
                            {
                                Vector3 pA = s_Cached2DVertices[s_CachedSelOutlineIndices[i]];
                                Vector3 pB = s_Cached2DVertices[s_CachedSelOutlineIndices[i + 1]];
                                GL.Vertex3(pA.x + pixelU, pA.y - pixelV, 0);
                                GL.Vertex3(pB.x + pixelU, pB.y - pixelV, 0);
                            }
                            GL.End();

                            // Pass 2: Luminous electric gold outer stroke
                            GL.Begin(GL.LINES);
                            GL.Color(new Color(1.0f, 0.85f, 0.20f, 1.0f));
                            for (int i = 0; i < s_CachedSelOutlineIndices.Length; i++)
                            {
                                GL.Vertex(s_Cached2DVertices[s_CachedSelOutlineIndices[i]]);
                            }
                            GL.End();
                        }

                        // C. Draw Selected Vertices (Golden Stars/Crosses)
                        if (s_CachedSelPointsIndices != null)
                        {
                            mat.SetPass(0);
                            DrawSelectedVertexMarkers(s_Cached2DVertices, s_CachedSelPointsIndices, halfSpanY * 0.018f, null, minX, maxX, minY, maxY);
                        }
                    }
                }

                GL.PopMatrix();
                RenderTexture.active = prevRT;

                // Draw Rendered Texture to GUI
                GUI.DrawTexture(canvasRect, s_PreviewRT, ScaleMode.StretchToFill);

                // Draw Border around canvas
                EditorGUI.DrawRect(new Rect(canvasRect.x, canvasRect.y, canvasRect.width, 1), new Color(0.25f, 0.25f, 0.25f, 1f));
                EditorGUI.DrawRect(new Rect(canvasRect.x, canvasRect.yMax - 1, canvasRect.width, 1), new Color(0.25f, 0.25f, 0.25f, 1f));
                EditorGUI.DrawRect(new Rect(canvasRect.x, canvasRect.y, 1, canvasRect.height), new Color(0.25f, 0.25f, 0.25f, 1f));
                EditorGUI.DrawRect(new Rect(canvasRect.xMax - 1, canvasRect.y, 1, canvasRect.height), new Color(0.25f, 0.25f, 0.25f, 1f));

                // Draw Marquee Selection Box if active
                if (s_IsMarqueeSelecting && s_HasDragMoved)
                {
                    float clampedMinX = Mathf.Clamp(Mathf.Min(s_MarqueeStartMouse.x, s_MarqueeCurrentMouse.x), canvasRect.x, canvasRect.xMax);
                    float clampedMaxX = Mathf.Clamp(Mathf.Max(s_MarqueeStartMouse.x, s_MarqueeCurrentMouse.x), canvasRect.x, canvasRect.xMax);
                    float clampedMinY = Mathf.Clamp(Mathf.Min(s_MarqueeStartMouse.y, s_MarqueeCurrentMouse.y), canvasRect.y, canvasRect.yMax);
                    float clampedMaxY = Mathf.Clamp(Mathf.Max(s_MarqueeStartMouse.y, s_MarqueeCurrentMouse.y), canvasRect.y, canvasRect.yMax);

                    Rect marqueeRect = Rect.MinMaxRect(clampedMinX, clampedMinY, clampedMaxX, clampedMaxY);

                    // Semi-transparent fill
                    EditorGUI.DrawRect(marqueeRect, new Color(0.25f, 0.65f, 1.0f, 0.20f));
                    // Borders
                    Color borderCol = new Color(0.35f, 0.8f, 1f, 0.9f);
                    EditorGUI.DrawRect(new Rect(marqueeRect.x, marqueeRect.y, marqueeRect.width, 1), borderCol);
                    EditorGUI.DrawRect(new Rect(marqueeRect.x, marqueeRect.yMax - 1, marqueeRect.width, 1), borderCol);
                    EditorGUI.DrawRect(new Rect(marqueeRect.x, marqueeRect.y, 1, marqueeRect.height), borderCol);
                    EditorGUI.DrawRect(new Rect(marqueeRect.xMax - 1, marqueeRect.y, 1, marqueeRect.height), borderCol);
                }
            }

            // IMGUI HUD Overlays (Top & Bottom badges)
            int selCount = selectedVertices != null ? selectedVertices.Count : 0;
            Vector2? ghostPos = (s_IsDraggingUVs && s_HasUVMoved) ? (s_DragOriginalCentroid + s_DragCurrentDeltaUV) : (Vector2?)null;
            DrawHUD(canvasRect, isHovered, hoverU, hoverV, viewZoom, channel, mesh, liveUVsOverride != null, selCount, s_IsDraggingUVs && s_HasUVMoved, s_DragCurrentDeltaUV, ghostPos, minX, maxX, minY, maxY, overlapInfo, selectedVertices, showOverlap, selectionMode, uvs, triangles);
        }

        private static void DrawGridLines(float minX, float maxX, float minY, float maxY, bool isLightMode)
        {
            Color minorGrid = isLightMode ? new Color(0.72f, 0.72f, 0.72f, 0.5f) : new Color(0.20f, 0.20f, 0.22f, 0.6f);
            Color majorGrid = isLightMode ? new Color(0.60f, 0.60f, 0.60f, 0.9f) : new Color(0.30f, 0.30f, 0.33f, 0.9f);

            float span = maxY - minY;
            float step = 0.1f;
            if (span > 5f) step = 1.0f;
            else if (span > 2f) step = 0.5f;
            else if (span > 0.8f) step = 0.25f;
            else if (span < 0.2f) step = 0.05f;

            int startX = Mathf.FloorToInt(minX / step);
            int endX = Mathf.CeilToInt(maxX / step);
            int startY = Mathf.FloorToInt(minY / step);
            int endY = Mathf.CeilToInt(maxY / step);

            GL.Begin(GL.LINES);

            // Vertical lines
            for (int i = startX; i <= endX; i++)
            {
                float x = i * step;
                bool isMajor = Mathf.Abs(x - Mathf.Round(x)) < 1e-4f;
                GL.Color(isMajor ? majorGrid : minorGrid);
                GL.Vertex3(x, minY, 0);
                GL.Vertex3(x, maxY, 0);
            }

            // Horizontal lines
            for (int i = startY; i <= endY; i++)
            {
                float y = i * step;
                bool isMajor = Mathf.Abs(y - Mathf.Round(y)) < 1e-4f;
                GL.Color(isMajor ? majorGrid : minorGrid);
                GL.Vertex3(minX, y, 0);
                GL.Vertex3(maxX, y, 0);
            }

            GL.End();
        }

        private static void DrawUnitSquareBounds()
        {
            Color boundsColor = new Color(1f, 0.8f, 0.2f, 0.85f);
            GL.Begin(GL.LINES);
            GL.Color(boundsColor);

            // [0,0] to [1,0]
            GL.Vertex3(0, 0, 0); GL.Vertex3(1, 0, 0);
            // [1,0] to [1,1]
            GL.Vertex3(1, 0, 0); GL.Vertex3(1, 1, 0);
            // [1,1] to [0,1]
            GL.Vertex3(1, 1, 0); GL.Vertex3(0, 1, 0);
            // [0,1] to [0,0]
            GL.Vertex3(0, 1, 0); GL.Vertex3(0, 0, 0);

            GL.End();
        }

        private static void DrawVertexMarkers(Vector3[] verts, float size, float minX = float.MinValue, float maxX = float.MaxValue, float minY = float.MinValue, float maxY = float.MaxValue)
        {
            Color dotColor = new Color(0.7f, 0.85f, 1f, 0.85f);
            GL.Begin(GL.LINES);
            GL.Color(dotColor);

            float pad = size * 2f;
            float cMinX = minX - pad;
            float cMaxX = maxX + pad;
            float cMinY = minY - pad;
            float cMaxY = maxY + pad;

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                if (v.x < cMinX || v.x > cMaxX || v.y < cMinY || v.y > cMaxY) continue;

                GL.Vertex3(v.x - size, v.y, 0);
                GL.Vertex3(v.x + size, v.y, 0);
                GL.Vertex3(v.x, v.y - size, 0);
                GL.Vertex3(v.x, v.y + size, 0);
            }

            GL.End();
        }

        private static void DrawSelectedVertexMarkers(Vector3[] verts, int[] indices, float size, Color? overrideColor = null, float minX = float.MinValue, float maxX = float.MaxValue, float minY = float.MinValue, float maxY = float.MaxValue)
        {
            if (verts == null || indices == null) return;
            Color dotColor = overrideColor ?? new Color(1f, 0.92f, 0.25f, 1f);
            GL.Begin(GL.LINES);
            GL.Color(dotColor);

            float pad = size * 2f;
            float cMinX = minX - pad;
            float cMaxX = maxX + pad;
            float cMinY = minY - pad;
            float cMaxY = maxY + pad;

            for (int k = 0; k < indices.Length; k++)
            {
                int i = indices[k];
                if (i < 0 || i >= verts.Length) continue;
                Vector3 v = verts[i];
                if (v.x < cMinX || v.x > cMaxX || v.y < cMinY || v.y > cMaxY) continue;

                // Cross
                GL.Vertex3(v.x - size, v.y, 0);
                GL.Vertex3(v.x + size, v.y, 0);
                GL.Vertex3(v.x, v.y - size, 0);
                GL.Vertex3(v.x, v.y + size, 0);

                // Diagonal cross (star marker)
                float diag = size * 0.7f;
                GL.Vertex3(v.x - diag, v.y - diag, 0);
                GL.Vertex3(v.x + diag, v.y + diag, 0);
                GL.Vertex3(v.x - diag, v.y + diag, 0);
                GL.Vertex3(v.x + diag, v.y - diag, 0);
            }

            GL.End();
        }

        private static GUIStyle s_HUDLabelStyle;
        private static GUIStyle s_HUDCenterStyle;
        private static GUIStyle s_HUDRightStyle;

        private static void EnsureHUDStyles()
        {
            if (s_HUDLabelStyle == null)
            {
                s_HUDLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    richText = true,
                    fontSize = 10,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = new Color(0.92f, 0.92f, 0.96f, 1f) }
                };
            }
            if (s_HUDCenterStyle == null)
            {
                s_HUDCenterStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    richText = true,
                    fontSize = 10,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.92f, 0.92f, 0.96f, 1f) }
                };
            }
            if (s_HUDRightStyle == null)
            {
                s_HUDRightStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    richText = true,
                    fontSize = 10,
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(0.78f, 0.78f, 0.82f, 1f) }
                };
            }
        }

        private static void DrawBadgeBox(Rect rect)
        {
            DrawBadgeBox(rect, new Color(0.12f, 0.12f, 0.15f, 0.90f), new Color(0.28f, 0.28f, 0.33f, 0.85f));
        }

        private static void DrawBadgeBox(Rect rect, Color bgColor, Color borderColor)
        {
            EditorGUI.DrawRect(rect, bgColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), borderColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), borderColor);
        }

        private static void DrawHUD(
            Rect canvasRect,
            bool isHovered,
            float hoverU,
            float hoverV,
            float zoom,
            int channel,
            Mesh mesh,
            bool isLivePreview,
            int selectedCount = 0,
            bool isMoving = false,
            Vector2 moveDelta = default,
            Vector2? ghostUVPos = null,
            float minX = 0f,
            float maxX = 1f,
            float minY = 0f,
            float maxY = 1f,
            IslandOverlapResult overlapInfo = null,
            HashSet<int> selectedVertices = null,
            bool showOverlapIndicators = true,
            UVSelectionMode selectionMode = UVSelectionMode.Island,
            Vector2[] uvs = null,
            int[] triangles = null)
        {
            // Strictly non-layout drawing on Repaint only: guarantees zero GUI layout mismatch errors
            if (Event.current.type != EventType.Repaint) return;

            EnsureHUDStyles();

            bool hasSelection = selectedCount > 0;
            s_ScratchSelIslandIds.Clear();
            int overlappingSelectedIslandCount = 0;
            int maxOverlapCount = 0;
            int maxSharedGroupSize = 0;

            if (overlapInfo != null && selectedVertices != null && selectedVertices.Count > 0)
            {
                foreach (int v in selectedVertices)
                {
                    if (overlapInfo.vertexToIslandMap.TryGetValue(v, out int islId) && islId >= 0 && islId < overlapInfo.islands.Count)
                    {
                        if (s_ScratchSelIslandIds.Add(islId))
                        {
                            var isl = overlapInfo.islands[islId];
                            if (isl.overlappingIslandIds.Count > 0)
                            {
                                overlappingSelectedIslandCount++;
                                maxOverlapCount = Mathf.Max(maxOverlapCount, isl.overlappingIslandIds.Count);
                                maxSharedGroupSize = Mathf.Max(maxSharedGroupSize, isl.overlappingIslandIds.Count + 1);
                            }
                        }
                    }
                }
            }

            // Check hovered island when in Island mode with no active selection
            int hoveredIslandId = -1;
            if (!hasSelection && isHovered && overlapInfo != null && uvs != null && triangles != null && selectionMode == UVSelectionMode.Island)
            {
                Vector2 hoverPoint = new Vector2(hoverU, hoverV);
                for (int i = 0; i < overlapInfo.islands.Count; i++)
                {
                    var isl = overlapInfo.islands[i];
                    if (isl.bounds.Contains(hoverPoint))
                    {
                        for (int t = 0; t < isl.triangleIndices.Count; t++)
                        {
                            int off = isl.triangleIndices[t];
                            if (off + 2 < triangles.Length)
                            {
                                if (PointInTriangle(hoverPoint, uvs[triangles[off]], uvs[triangles[off + 1]], uvs[triangles[off + 2]]))
                                {
                                    hoveredIslandId = isl.id;
                                    break;
                                }
                            }
                        }
                        if (hoveredIslandId >= 0) break;
                    }
                }
            }

            // 1. Top-left HUD badge: Active Channel & Status
            float tlWidth = isLivePreview ? 210 : 110;
            Rect tlRect = new Rect(canvasRect.x + 8, canvasRect.y + 8, tlWidth, 22);
            DrawBadgeBox(tlRect);
            string tlText = isLivePreview
                ? $"<b>Channel:</b> UV{channel}  <color=#ff9933>• LIVE PREVIEW</color>"
                : $"<b>Channel:</b> UV{channel}";
            GUI.Label(new Rect(tlRect.x + 6, tlRect.y + 1, tlRect.width - 12, tlRect.height - 2), tlText, s_HUDLabelStyle);

            // 2. Selection / Moving Badge (Next to Channel Badge on Row 1)
            if (isMoving)
            {
                Rect selRect = new Rect(tlRect.xMax + 6, canvasRect.y + 8, 250, 22);
                DrawBadgeBox(selRect);
                string moveSignU = moveDelta.x >= 0 ? "+" : "";
                string moveSignV = moveDelta.y >= 0 ? "+" : "";
                GUI.Label(new Rect(selRect.x + 6, selRect.y + 1, selRect.width - 12, selRect.height - 2),
                    $"<color=#38d9a9><b>Ghost Preview:</b> ΔU {moveSignU}{moveDelta.x:F3}  ΔV {moveSignV}{moveDelta.y:F3}</color>", s_HUDLabelStyle);

                // Floating Ghost Badge tracking the ghost preview centroid
                if (ghostUVPos.HasValue)
                {
                    Vector2 gUV = ghostUVPos.Value;
                    float screenX = canvasRect.x + ((gUV.x - minX) / Mathf.Max(0.0001f, maxX - minX)) * canvasRect.width;
                    float screenY = canvasRect.y + (1f - (gUV.y - minY) / Mathf.Max(0.0001f, maxY - minY)) * canvasRect.height;

                    Rect floatRect = new Rect(
                        Mathf.Clamp(screenX + 16f, canvasRect.x + 8f, canvasRect.xMax - 185f),
                        Mathf.Clamp(screenY - 26f, canvasRect.y + 8f, canvasRect.yMax - 30f),
                        175f, 22f
                    );
                    DrawBadgeBox(floatRect);
                    GUI.Label(new Rect(floatRect.x + 6, floatRect.y + 1, floatRect.width - 12, floatRect.height - 2),
                        $"<color=#38d9a9><b>Ghost:</b> ΔU {moveSignU}{moveDelta.x:F3}  ΔV {moveSignV}{moveDelta.y:F3}</color>", s_HUDLabelStyle);
                }
            }
            else if (hasSelection)
            {
                string modeName = selectionMode == UVSelectionMode.Quad ? "Quad" : (selectionMode == UVSelectionMode.Face ? "Face" : (selectionMode == UVSelectionMode.Island ? "Island" : "Vertex"));
                string selText = $"<color=#ffd24d>★ Selected: {modeName} ({selectedCount:N0} verts)</color>";
                float selWidth = Mathf.Clamp(s_HUDLabelStyle.CalcSize(new GUIContent(selText)).x + 16f, 150f, 220f);
                Rect selRect = new Rect(tlRect.xMax + 6, canvasRect.y + 8, selWidth, 22);
                DrawBadgeBox(selRect);
                GUI.Label(new Rect(selRect.x + 6, selRect.y + 1, selRect.width - 12, selRect.height - 2), selText, s_HUDLabelStyle);
            }
            else if (showOverlapIndicators && overlapInfo != null)
            {
                // Global overlap summary on Row 1 when idle (no active selection)
                float ovWidth = overlapInfo.totalOverlappingIslands > 0 ? 230f : 100f;
                Rect ovRect = new Rect(tlRect.xMax + 6, canvasRect.y + 8, ovWidth, 22);
                if (ovRect.xMax < canvasRect.xMax - 105)
                {
                    if (overlapInfo.totalOverlappingIslands > 0)
                    {
                        DrawBadgeBox(ovRect, new Color(0.18f, 0.12f, 0.05f, 0.92f), new Color(1.0f, 0.65f, 0.20f, 0.85f));
                        GUI.Label(new Rect(ovRect.x + 6, ovRect.y + 1, ovRect.width - 12, ovRect.height - 2),
                            $"<color=#ffaa33><b>🗂 Overlaps:</b> {overlapInfo.totalOverlappingIslands} islands ({overlapInfo.sharedGroups.Count} shared)</color>", s_HUDLabelStyle);
                    }
                    else
                    {
                        DrawBadgeBox(ovRect);
                        GUI.Label(new Rect(ovRect.x + 6, ovRect.y + 1, ovRect.width - 12, ovRect.height - 2),
                            "<color=#88cc88>✓ 0 Overlaps</color>", s_HUDLabelStyle);
                    }
                }
            }

            // 3. Top of the View Overlap Indicator Banner when selecting UV island (or hovering)
            if (showOverlapIndicators && overlapInfo != null && (hasSelection || hoveredIslandId >= 0))
            {
                float bannerW = Mathf.Clamp(canvasRect.width - 24f, 240f, 440f);
                float bannerX = canvasRect.x + (canvasRect.width - bannerW) * 0.5f;
                Rect topBannerRect = new Rect(bannerX, canvasRect.y + 34, bannerW, 22f);

                if (hasSelection)
                {
                    if (overlappingSelectedIslandCount > 0)
                    {
                        DrawBadgeBox(topBannerRect, new Color(0.20f, 0.12f, 0.04f, 0.95f), new Color(1.0f, 0.65f, 0.15f, 0.95f));
                        string msg = s_ScratchSelIslandIds.Count == 1
                            ? $"<b><color=#ffaa22>🗂 OVERLAP DETECTED:</color></b> <color=#ffffff>Selected Island overlaps </color><b><color=#ffcc44>{maxOverlapCount} other island{(maxOverlapCount > 1 ? "s" : "")}</color></b> <color=#ffaa22>• Shared Texture ({maxSharedGroupSize}x)</color>"
                            : $"<b><color=#ffaa22>🗂 OVERLAP DETECTED:</color></b> <color=#ffffff>{overlappingSelectedIslandCount} of {s_ScratchSelIslandIds.Count} selected islands overlap other charts</color>";
                        GUI.Label(new Rect(topBannerRect.x + 4, topBannerRect.y + 1, topBannerRect.width - 8, topBannerRect.height - 2), msg, s_HUDCenterStyle);
                    }
                    else
                    {
                        DrawBadgeBox(topBannerRect, new Color(0.06f, 0.14f, 0.08f, 0.92f), new Color(0.35f, 0.85f, 0.45f, 0.85f));
                        string msg = s_ScratchSelIslandIds.Count == 1
                            ? "<b><color=#66dd88>✓ NO OVERLAP:</color></b> <color=#e0e0e0>Selected Island has unique texture space (0 overlaps)</color>"
                            : $"<b><color=#66dd88>✓ NO OVERLAPS:</color></b> <color=#e0e0e0>All {s_ScratchSelIslandIds.Count} selected islands have unique texture space</color>";
                        GUI.Label(new Rect(topBannerRect.x + 4, topBannerRect.y + 1, topBannerRect.width - 8, topBannerRect.height - 2), msg, s_HUDCenterStyle);
                    }
                }
                else if (hoveredIslandId >= 0)
                {
                    var hIsl = overlapInfo.islands[hoveredIslandId];
                    if (hIsl.overlappingIslandIds.Count > 0)
                    {
                        DrawBadgeBox(topBannerRect, new Color(0.18f, 0.11f, 0.04f, 0.92f), new Color(1.0f, 0.65f, 0.15f, 0.85f));
                        string msg = $"<b><color=#ffaa22>🗂 HOVERED ISLAND:</color></b> <color=#ffffff>Overlaps </color><b><color=#ffcc44>{hIsl.overlappingIslandIds.Count} other island{(hIsl.overlappingIslandIds.Count > 1 ? "s" : "")}</color></b> <color=#ffaa22>• Shared ({hIsl.overlappingIslandIds.Count + 1}x)</color>";
                        GUI.Label(new Rect(topBannerRect.x + 4, topBannerRect.y + 1, topBannerRect.width - 8, topBannerRect.height - 2), msg, s_HUDCenterStyle);
                    }
                    else
                    {
                        DrawBadgeBox(topBannerRect, new Color(0.06f, 0.14f, 0.08f, 0.90f), new Color(0.35f, 0.85f, 0.45f, 0.80f));
                        string msg = "<b><color=#66dd88>✓ HOVERED ISLAND:</color></b> <color=#e0e0e0>Unique texture space (0 overlaps)</color>";
                        GUI.Label(new Rect(topBannerRect.x + 4, topBannerRect.y + 1, topBannerRect.width - 8, topBannerRect.height - 2), msg, s_HUDCenterStyle);
                    }
                }
            }

            // 4. Top-right HUD badge: Zoom level
            Rect trRect = new Rect(canvasRect.xMax - 100, canvasRect.y + 8, 92, 22);
            DrawBadgeBox(trRect);
            GUI.Label(new Rect(trRect.x + 4, trRect.y + 1, trRect.width - 8, trRect.height - 2), $"Zoom: {Mathf.RoundToInt(zoom * 100f)}%", s_HUDCenterStyle);

            // 5. Bottom-left HUD badge: Live Hover UV coordinates (only when hovered)
            if (isHovered)
            {
                Rect blRect = new Rect(canvasRect.x + 8, canvasRect.yMax - 28, 220, 22);
                DrawBadgeBox(blRect);
                GUI.Label(new Rect(blRect.x + 6, blRect.y + 1, blRect.width - 12, blRect.height - 2), $"<b>U:</b> {hoverU:F4}   <b>V:</b> {hoverV:F4}", s_HUDLabelStyle);
            }

            // 7. Bottom-right Navigation help
            float brWidth = 268f;
            Rect brRect = new Rect(canvasRect.xMax - brWidth - 8, canvasRect.yMax - 28, brWidth, 22);
            DrawBadgeBox(brRect);
            string helpText = isMoving ? "Ghost Preview | Drag | Esc/R-Click: Cancel" : "[L] Island | Drag | R-Click: Unselect";
            GUI.Label(new Rect(brRect.x + 4, brRect.y + 1, brRect.width - 8, brRect.height - 2), helpText, s_HUDRightStyle);
        }

        #endregion

        #region Scene View 3D Visualization & Interaction

        public static bool RayIntersectsTriangle(
            Vector3 rayOrigin, Vector3 rayDir,
            Vector3 v0, Vector3 v1, Vector3 v2,
            out float distance)
        {
            distance = 0f;
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 pvec = Vector3.Cross(rayDir, edge2);
            float det = Vector3.Dot(edge1, pvec);

            if (Mathf.Abs(det) < 1e-6f) return false;
            float invDet = 1.0f / det;

            Vector3 tvec = rayOrigin - v0;
            float u = Vector3.Dot(tvec, pvec) * invDet;
            if (u < 0f || u > 1f) return false;

            Vector3 qvec = Vector3.Cross(tvec, edge1);
            float v = Vector3.Dot(rayDir, qvec) * invDet;
            if (v < 0f || u + v > 1f) return false;

            distance = Vector3.Dot(edge2, qvec) * invDet;
            return distance > 1e-4f;
        }

        public static bool RaycastMeshTriangles(
            Ray localRay,
            Vector3[] vertices,
            int[] triangles,
            out int hitTriangleIndex,
            out float hitDistance)
        {
            hitTriangleIndex = -1;
            hitDistance = float.MaxValue;
            if (vertices == null || triangles == null) return false;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                if (i0 < 0 || i0 >= vertices.Length ||
                    i1 < 0 || i1 >= vertices.Length ||
                    i2 < 0 || i2 >= vertices.Length) continue;

                if (RayIntersectsTriangle(localRay.origin, localRay.direction, vertices[i0], vertices[i1], vertices[i2], out float dist))
                {
                    if (dist < hitDistance)
                    {
                        hitDistance = dist;
                        hitTriangleIndex = i;
                    }
                }
            }
            return hitTriangleIndex >= 0;
        }

        public static bool DeselectFromSceneRay(
            Ray localRay,
            Mesh mesh,
            int submeshIndex,
            int channel,
            HashSet<int> selectedVertices,
            UVSelectionMode selectionMode,
            bool altKey = false,
            bool selectConnectedFBX = true)
        {
            if (selectedVertices == null || selectedVertices.Count == 0 || mesh == null) return false;

            int initialCount = selectedVertices.Count;
            Vector3[] meshVerts = GetCachedMeshVertices(mesh);
            int[] triangles = GetTriangles(mesh, submeshIndex);
            Vector2[] uvs = GetUVs(mesh, channel);

            bool hit = RaycastMeshTriangles(localRay, meshVerts, triangles, out int hitTriIdx, out float hitDist);
            if (!hit && submeshIndex >= 0 && mesh.subMeshCount > 1)
            {
                int[] allTris = GetTriangles(mesh, -1);
                if (RaycastMeshTriangles(localRay, meshVerts, allTris, out hitTriIdx, out hitDist))
                {
                    hit = true;
                    triangles = allTris;
                }
            }

            if (hit && hitTriIdx >= 0 && hitTriIdx + 2 < triangles.Length)
            {
                HashSet<int> targetVertices = new HashSet<int>();

                if (selectionMode == UVSelectionMode.Vertex)
                {
                    Vector3 hitPt = localRay.origin + localRay.direction * hitDist;
                    int i0 = triangles[hitTriIdx];
                    int i1 = triangles[hitTriIdx + 1];
                    int i2 = triangles[hitTriIdx + 2];

                    int closest = i0;
                    float d0 = (meshVerts[i0] - hitPt).sqrMagnitude;
                    float d1 = (meshVerts[i1] - hitPt).sqrMagnitude;
                    float d2 = (meshVerts[i2] - hitPt).sqrMagnitude;
                    if (d1 < d0 && d1 < d2) closest = i1;
                    else if (d2 < d0 && d2 < d1) closest = i2;

                    if (selectConnectedFBX)
                    {
                        targetVertices = GetConnectedVertices(closest, meshVerts, uvs, match3DPosition: true, matchUVPosition: true);
                    }
                    else
                    {
                        targetVertices.Add(closest);
                    }
                }
                else if (selectionMode == UVSelectionMode.Quad || selectionMode == UVSelectionMode.Face)
                {
                    if (selectionMode == UVSelectionMode.Quad || !altKey)
                    {
                        targetVertices = GetQuadVertices(hitTriIdx, uvs, triangles, meshVerts);
                    }
                    else
                    {
                        targetVertices.Add(triangles[hitTriIdx]);
                        targetVertices.Add(triangles[hitTriIdx + 1]);
                        targetVertices.Add(triangles[hitTriIdx + 2]);
                    }
                }
                else // UVSelectionMode.Island
                {
                    int seedVert = triangles[hitTriIdx];
                    targetVertices = GetIslandVertices(seedVert, uvs, triangles, meshVerts);
                }

                if (targetVertices.Count > 0 && targetVertices.Overlaps(selectedVertices))
                {
                    selectedVertices.ExceptWith(targetVertices);
                }
                else
                {
                    selectedVertices.Clear();
                }
            }
            else
            {
                selectedVertices.Clear();
            }

            bool changed = selectedVertices.Count != initialCount;
            if (changed)
            {
                InvalidateCache();
            }
            return changed;
        }

        private static void AddInternalEdgeIfNew(int a, int b, HashSet<long> boundarySet, HashSet<long> recorded, List<int> list)
        {
            long key = a < b ? (((long)a << 32) | (uint)b) : (((long)b << 32) | (uint)a);
            if (!boundarySet.Contains(key) && recorded.Add(key))
            {
                list.Add(a);
                list.Add(b);
            }
        }

        private static Material s_SceneHighlightMaterial;

        private static Material GetSceneHighlightMaterial(CompareFunction zTest)
        {
            if (s_SceneHighlightMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    s_SceneHighlightMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
            }
            if (s_SceneHighlightMaterial != null)
            {
                s_SceneHighlightMaterial.SetInt("_ZTest", (int)zTest);
                s_SceneHighlightMaterial.SetPass(0);
            }
            return s_SceneHighlightMaterial;
        }

        private static Vector3 GetCachedVertexNormal(int idx, List<Vector3> normals, Vector3 defaultNormal)
        {
            if (normals != null && idx >= 0 && idx < normals.Count && normals[idx].sqrMagnitude > 0.001f)
            {
                return normals[idx];
            }
            return defaultNormal;
        }

        private static void UpdateSceneHighlightCache(
            Transform transform,
            Mesh mesh,
            int submeshIndex,
            int channel,
            HashSet<int> selectedVertices,
            UVSelectionMode selectionMode,
            bool showUVLabels,
            int targetId,
            int meshId,
            int selHash)
        {
            s_SceneCache.isValid = false;
            if (mesh == null || selectedVertices == null || selectedVertices.Count == 0) return;

            // 1. Retrieve mesh data non-allocatingly
            s_ScratchVerts.Clear();
            mesh.GetVertices(s_ScratchVerts);
            if (s_ScratchVerts.Count == 0) return;

            s_ScratchNormals.Clear();
            mesh.GetNormals(s_ScratchNormals);

            s_ScratchUVs.Clear();
            mesh.GetUVs(channel, s_ScratchUVs);

            s_ScratchTriangles.Clear();
            if (submeshIndex >= 0 && submeshIndex < mesh.subMeshCount)
            {
                mesh.GetTriangles(s_ScratchTriangles, submeshIndex);
            }
            else
            {
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    s_ScratchSubmeshTris.Clear();
                    mesh.GetTriangles(s_ScratchSubmeshTris, s);
                    s_ScratchTriangles.AddRange(s_ScratchSubmeshTris);
                }
            }

            if (s_ScratchTriangles.Count == 0) return;

            // 2. Selected Triangles
            s_ScratchSelTris.Clear();
            for (int i = 0; i < s_ScratchTriangles.Count; i += 3)
            {
                int t0 = s_ScratchTriangles[i];
                int t1 = s_ScratchTriangles[i + 1];
                int t2 = s_ScratchTriangles[i + 2];
                if (selectedVertices.Contains(t0) && selectedVertices.Contains(t1) && selectedVertices.Contains(t2))
                {
                    s_ScratchSelTris.Add(t0);
                    s_ScratchSelTris.Add(t1);
                    s_ScratchSelTris.Add(t2);
                }
            }

            // 3. Fallback / average normal
            Vector3 triNorm = Vector3.up;
            if (s_ScratchSelTris.Count >= 3)
            {
                int i0 = s_ScratchSelTris[0];
                int i1 = s_ScratchSelTris[1];
                int i2 = s_ScratchSelTris[2];
                if (i0 < s_ScratchVerts.Count && i1 < s_ScratchVerts.Count && i2 < s_ScratchVerts.Count)
                {
                    Vector3 cross = Vector3.Cross(s_ScratchVerts[i1] - s_ScratchVerts[i0], s_ScratchVerts[i2] - s_ScratchVerts[i0]);
                    if (cross.sqrMagnitude > 1e-6f)
                        triNorm = cross.normalized;
                }
            }

            float normalPush = 0.0015f;

            // 4. Fill Vertices
            Vector3[] fillVerts = new Vector3[s_ScratchSelTris.Count];
            for (int i = 0; i < s_ScratchSelTris.Count; i++)
            {
                int vIdx = s_ScratchSelTris[i];
                Vector3 v = (vIdx >= 0 && vIdx < s_ScratchVerts.Count) ? s_ScratchVerts[vIdx] : Vector3.zero;
                Vector3 n = GetCachedVertexNormal(vIdx, s_ScratchNormals, triNorm);
                fillVerts[i] = v + n * normalPush;
            }
            s_SceneCache.fillVertices = fillVerts;

            // 5. Boundary and Internal Edges
            Dictionary<long, int> edgeUsage = new Dictionary<long, int>();
            Dictionary<long, KeyValuePair<int, int>> edgeMap = new Dictionary<long, KeyValuePair<int, int>>();

            for (int i = 0; i < s_ScratchSelTris.Count; i += 3)
            {
                int t0 = s_ScratchSelTris[i];
                int t1 = s_ScratchSelTris[i + 1];
                int t2 = s_ScratchSelTris[i + 2];
                RecordOutlineEdge(t0, t1, edgeUsage, edgeMap);
                RecordOutlineEdge(t1, t2, edgeUsage, edgeMap);
                RecordOutlineEdge(t2, t0, edgeUsage, edgeMap);
            }

            s_ScratchBoundaryEdges.Clear();
            s_ScratchInternalEdges.Clear();

            foreach (var kvp in edgeUsage)
            {
                var pair = edgeMap[kvp.Key];
                if (kvp.Value == 1)
                {
                    s_ScratchBoundaryEdges.Add(pair.Key);
                    s_ScratchBoundaryEdges.Add(pair.Value);
                }
                else
                {
                    s_ScratchInternalEdges.Add(pair.Key);
                    s_ScratchInternalEdges.Add(pair.Value);
                }
            }

            // Boundary vertices
            Vector3[] boundaryVerts = new Vector3[s_ScratchBoundaryEdges.Count];
            for (int i = 0; i < s_ScratchBoundaryEdges.Count; i++)
            {
                int vIdx = s_ScratchBoundaryEdges[i];
                Vector3 v = (vIdx >= 0 && vIdx < s_ScratchVerts.Count) ? s_ScratchVerts[vIdx] : Vector3.zero;
                Vector3 n = GetCachedVertexNormal(vIdx, s_ScratchNormals, triNorm);
                boundaryVerts[i] = v + n * (normalPush * 1.5f);
            }
            s_SceneCache.boundaryVertices = boundaryVerts;

            // Internal vertices
            Vector3[] internalVerts = new Vector3[s_ScratchInternalEdges.Count];
            for (int i = 0; i < s_ScratchInternalEdges.Count; i++)
            {
                int vIdx = s_ScratchInternalEdges[i];
                Vector3 v = (vIdx >= 0 && vIdx < s_ScratchVerts.Count) ? s_ScratchVerts[vIdx] : Vector3.zero;
                Vector3 n = GetCachedVertexNormal(vIdx, s_ScratchNormals, triNorm);
                internalVerts[i] = v + n * (normalPush * 1.2f);
            }
            s_SceneCache.internalVertices = internalVerts;

            // 6. Vertex handles (DotHandleCap positions)
            List<Vector3> handles = new List<Vector3>(selectedVertices.Count);
            foreach (int vIdx in selectedVertices)
            {
                if (vIdx >= 0 && vIdx < s_ScratchVerts.Count)
                {
                    Vector3 v = s_ScratchVerts[vIdx];
                    Vector3 n = GetCachedVertexNormal(vIdx, s_ScratchNormals, triNorm);
                    handles.Add(v + n * (normalPush * 2.0f));
                }
            }
            s_SceneCache.vertexHandles = handles.ToArray();

            // 7. Centroid & Readouts
            Vector3 localCentroid = Vector3.zero;
            Vector3 localNormal = Vector3.zero;
            Vector2 uvCentroid = Vector2.zero;
            Vector2 uvMin = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 uvMax = new Vector2(float.MinValue, float.MinValue);
            int validCount = 0;

            foreach (int vIdx in selectedVertices)
            {
                if (vIdx >= 0 && vIdx < s_ScratchVerts.Count)
                {
                    localCentroid += s_ScratchVerts[vIdx];
                    if (vIdx < s_ScratchNormals.Count)
                        localNormal += s_ScratchNormals[vIdx];
                    if (vIdx < s_ScratchUVs.Count)
                    {
                        Vector2 uv = s_ScratchUVs[vIdx];
                        uvCentroid += uv;
                        uvMin = Vector2.Min(uvMin, uv);
                        uvMax = Vector2.Max(uvMax, uv);
                    }
                    validCount++;
                }
            }

            if (validCount > 0)
            {
                localCentroid /= validCount;
                localNormal = (localNormal.sqrMagnitude > 0.001f) ? localNormal.normalized : triNorm;
                uvCentroid /= validCount;

                s_SceneCache.localCentroid = localCentroid;
                s_SceneCache.localNormal = localNormal;

                string modeName = selectionMode == UVSelectionMode.Quad ? "Quad (4 Verts)" :
                                  (selectionMode == UVSelectionMode.Face ? "Face (3 Verts)" :
                                  (selectionMode == UVSelectionMode.Island ? "UV Island" : $"{selectedVertices.Count} Vertices"));

                int selOverlapCount = 0;
                int selSharedGroupSize = 0;
                IslandOverlapResult overlapInfo = GetIslandOverlapInfo(mesh, channel, submeshIndex);
                if (overlapInfo != null)
                {
                    foreach (int v in selectedVertices)
                    {
                        if (overlapInfo.vertexToIslandMap.TryGetValue(v, out int islId) && islId >= 0 && islId < overlapInfo.islands.Count)
                        {
                            var isl = overlapInfo.islands[islId];
                            if (isl.overlappingIslandIds.Count > 0)
                            {
                                selOverlapCount = isl.overlappingIslandIds.Count;
                                selSharedGroupSize = isl.overlappingIslandIds.Count + 1;
                                break;
                            }
                        }
                    }
                }

                s_SceneCache.overlapCount = selOverlapCount;
                s_SceneCache.sharedGroupSize = selSharedGroupSize;

                if (selOverlapCount > 0)
                {
                    s_SceneCache.badgeTitle = $"★ {modeName} (🗂 Shared {selSharedGroupSize}x)";
                    s_SceneCache.badgeUV = $"UV [{uvMin.x:F3}, {uvMin.y:F3}] → [{uvMax.x:F3}, {uvMax.y:F3}] (🗂 Overlaps {selOverlapCount})";
                }
                else
                {
                    s_SceneCache.badgeTitle = $"★ {modeName}";
                    s_SceneCache.badgeUV = $"UV [{uvMin.x:F3}, {uvMin.y:F3}] → [{uvMax.x:F3}, {uvMax.y:F3}]";
                }

                if (showUVLabels && selectedVertices.Count <= 8 && s_ScratchUVs.Count > 0)
                {
                    List<SceneHighlightCache.CornerChip> chips = new List<SceneHighlightCache.CornerChip>();
                    foreach (int vi in selectedVertices)
                    {
                        if (vi >= 0 && vi < s_ScratchVerts.Count && vi < s_ScratchUVs.Count)
                        {
                            Vector2 uvCoord = s_ScratchUVs[vi];
                            chips.Add(new SceneHighlightCache.CornerChip
                            {
                                localPos = s_ScratchVerts[vi],
                                uvText = $"({uvCoord.x:F3}, {uvCoord.y:F3})"
                            });
                        }
                    }
                    s_SceneCache.cornerChips = chips.ToArray();
                }
                else
                {
                    s_SceneCache.cornerChips = null;
                }
            }
            else
            {
                s_SceneCache.cornerChips = null;
                s_SceneCache.badgeTitle = null;
                s_SceneCache.badgeUV = null;
                s_SceneCache.overlapCount = 0;
                s_SceneCache.sharedGroupSize = 0;
            }

            s_SceneCache.targetInstanceId = targetId;
            s_SceneCache.meshInstanceId = meshId;
            s_SceneCache.submeshIndex = submeshIndex;
            s_SceneCache.channel = channel;
            s_SceneCache.selectionHash = selHash;
            s_SceneCache.selectionMode = selectionMode;
            s_SceneCache.showUVLabels = showUVLabels;
            s_SceneCache.isValid = true;
        }

        private static bool s_SceneRMBDown = false;
        private static Vector2 s_SceneRMBDownPos = Vector2.zero;
        private static double s_SceneRMBDownTime = 0.0;
        private static bool s_SceneRMBDragged = false;
        private static double s_LastRMBDragTime = 0.0;

        private static void HandleSceneViewRightClickDeselect(
            Transform transform,
            Mesh mesh,
            int submeshIndex,
            int channel,
            HashSet<int> selectedVertices,
            UVSelectionMode selectionMode,
            Action onSelectionChanged,
            bool selectConnectedFBX = true)
        {
            if (selectedVertices == null || selectedVertices.Count == 0 || transform == null || mesh == null) return;

            Event evt = Event.current;
            if (evt == null) return;

            if (evt.type == EventType.MouseDown && evt.button == 1)
            {
                s_SceneRMBDown = true;
                s_SceneRMBDownPos = evt.mousePosition;
                s_SceneRMBDownTime = EditorApplication.timeSinceStartup;
                s_SceneRMBDragged = false;
            }
            else if (evt.type == EventType.MouseDrag && evt.button == 1)
            {
                if (s_SceneRMBDown)
                {
                    if ((evt.mousePosition - s_SceneRMBDownPos).sqrMagnitude > 16f)
                    {
                        s_SceneRMBDragged = true;
                        s_LastRMBDragTime = EditorApplication.timeSinceStartup;
                    }
                }
            }
            else if (evt.type == EventType.MouseUp && evt.button == 1)
            {
                if (s_SceneRMBDown)
                {
                    bool wasClick = !s_SceneRMBDragged &&
                                    (evt.mousePosition - s_SceneRMBDownPos).sqrMagnitude < 25f &&
                                    (EditorApplication.timeSinceStartup - s_SceneRMBDownTime) < 0.5 &&
                                    (EditorApplication.timeSinceStartup - s_LastRMBDragTime) > 0.2;

                    s_SceneRMBDown = false;
                    s_SceneRMBDragged = false;

                    if (wasClick)
                    {
                        ExecuteSceneViewDeselect(transform, mesh, submeshIndex, channel, selectedVertices, selectionMode, onSelectionChanged, selectConnectedFBX);
                        evt.Use();
                        GUI.changed = true;
                    }
                }
            }
            else if (evt.type == EventType.ContextClick)
            {
                if (!s_SceneRMBDragged && (EditorApplication.timeSinceStartup - s_LastRMBDragTime) > 0.2)
                {
                    s_SceneRMBDown = false;
                    s_SceneRMBDragged = false;
                    ExecuteSceneViewDeselect(transform, mesh, submeshIndex, channel, selectedVertices, selectionMode, onSelectionChanged, selectConnectedFBX);
                    evt.Use();
                    GUI.changed = true;
                }
            }
        }

        private static void ExecuteSceneViewDeselect(
            Transform transform,
            Mesh mesh,
            int submeshIndex,
            int channel,
            HashSet<int> selectedVertices,
            UVSelectionMode selectionMode,
            Action onSelectionChanged,
            bool selectConnectedFBX = true)
        {
            if (selectedVertices == null || selectedVertices.Count == 0 || transform == null || mesh == null) return;

            Event evt = Event.current;
            Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            Vector3 localOrigin = transform.InverseTransformPoint(worldRay.origin);
            Vector3 localDir = transform.InverseTransformVector(worldRay.direction).normalized;
            Ray localRay = new Ray(localOrigin, localDir);

            bool changed = DeselectFromSceneRay(localRay, mesh, submeshIndex, channel, selectedVertices, selectionMode, evt != null && evt.alt, selectConnectedFBX);
            if (changed)
            {
                onSelectionChanged?.Invoke();
                SceneView.RepaintAll();
            }
        }

        public static void DrawSceneGUI(
            Transform transform,
            Mesh mesh,
            int submeshIndex,
            int channel,
            HashSet<int> selectedVertices,
            UVSelectionMode selectionMode,
            bool showHighlight = true,
            bool showUVLabels = true,
            Color? highlightColor = null,
            Action onSelectionChanged = null,
            bool selectConnectedFBX = true)
        {
            if (transform == null || mesh == null) return;

            Event evt = Event.current;

            // Handle Right Click in Scene View: Deselect UV
            HandleSceneViewRightClickDeselect(
                transform,
                mesh,
                submeshIndex,
                channel,
                selectedVertices,
                selectionMode,
                onSelectionChanged,
                selectConnectedFBX
            );

            // Pure Display in SceneView on Repaint: Selection display and highlights
            if (evt.type != EventType.Repaint) return;
            if (!showHighlight || selectedVertices == null || selectedVertices.Count == 0) return;

            int targetId = transform.GetInstanceID();
            int meshId = mesh.GetInstanceID();
            int selHash = ComputeSelectionHash(selectedVertices);

            bool cacheNeedsUpdate = !s_SceneCache.isValid ||
                                    s_SceneCache.targetInstanceId != targetId ||
                                    s_SceneCache.meshInstanceId != meshId ||
                                    s_SceneCache.submeshIndex != submeshIndex ||
                                    s_SceneCache.channel != channel ||
                                    s_SceneCache.selectionHash != selHash ||
                                    s_SceneCache.selectionMode != selectionMode ||
                                    s_SceneCache.showUVLabels != showUVLabels;

            if (cacheNeedsUpdate)
            {
                UpdateSceneHighlightCache(transform, mesh, submeshIndex, channel, selectedVertices, selectionMode, showUVLabels, targetId, meshId, selHash);
            }

            if (!s_SceneCache.isValid) return;

            Color baseCol = highlightColor ?? new Color(1.0f, 0.75f, 0.18f, 0.45f);
            Color frontFillCol = new Color(baseCol.r, baseCol.g, baseCol.b, Mathf.Clamp01(baseCol.a * 0.9f));
            Color xRayFillCol = new Color(baseCol.r, baseCol.g, baseCol.b, 0.12f);

            Color frontOutlineCol = new Color(1.0f, 0.95f, 0.35f, 0.95f);
            Color xRayOutlineCol = new Color(1.0f, 0.85f, 0.35f, 0.30f);
            Color frontInternalCol = new Color(1.0f, 0.85f, 0.30f, 0.45f);

            Matrix4x4 localToWorld = transform.localToWorldMatrix;

            // A. Surface Fill
            Vector3[] fillVerts = s_SceneCache.fillVertices;
            if (fillVerts != null && fillVerts.Length > 0)
            {
                // Pass 1: X-Ray Occluded (Behind other geometry)
                GetSceneHighlightMaterial(CompareFunction.Greater);
                GL.PushMatrix();
                GL.MultMatrix(localToWorld);
                GL.Begin(GL.TRIANGLES);
                GL.Color(xRayFillCol);
                for (int i = 0; i < fillVerts.Length; i++)
                {
                    GL.Vertex(fillVerts[i]);
                }
                GL.End();
                GL.PopMatrix();

                // Pass 2: Front Unoccluded (Directly visible surface)
                GetSceneHighlightMaterial(CompareFunction.LessEqual);
                GL.PushMatrix();
                GL.MultMatrix(localToWorld);
                GL.Begin(GL.TRIANGLES);
                GL.Color(frontFillCol);
                for (int i = 0; i < fillVerts.Length; i++)
                {
                    GL.Vertex(fillVerts[i]);
                }
                GL.End();
                GL.PopMatrix();
            }

            // B. Boundary Silhouette Outline
            Vector3[] boundaryVerts = s_SceneCache.boundaryVertices;
            if (boundaryVerts != null && boundaryVerts.Length > 0)
            {
                // X-Ray Boundary Outline
                GetSceneHighlightMaterial(CompareFunction.Greater);
                GL.PushMatrix();
                GL.MultMatrix(localToWorld);
                GL.Begin(GL.LINES);
                GL.Color(xRayOutlineCol);
                for (int i = 0; i < boundaryVerts.Length; i++)
                {
                    GL.Vertex(boundaryVerts[i]);
                }
                GL.End();
                GL.PopMatrix();

                // Front Boundary Outline
                GetSceneHighlightMaterial(CompareFunction.LessEqual);
                GL.PushMatrix();
                GL.MultMatrix(localToWorld);
                GL.Begin(GL.LINES);
                GL.Color(frontOutlineCol);
                for (int i = 0; i < boundaryVerts.Length; i++)
                {
                    GL.Vertex(boundaryVerts[i]);
                }
                GL.End();
                GL.PopMatrix();
            }

            // C. Internal Wireframe Edges (e.g. quad diagonal)
            Vector3[] internalVerts = s_SceneCache.internalVertices;
            if (internalVerts != null && internalVerts.Length > 0)
            {
                GetSceneHighlightMaterial(CompareFunction.LessEqual);
                GL.PushMatrix();
                GL.MultMatrix(localToWorld);
                GL.Begin(GL.LINES);
                GL.Color(frontInternalCol);
                for (int i = 0; i < internalVerts.Length; i++)
                {
                    GL.Vertex(internalVerts[i]);
                }
                GL.End();
                GL.PopMatrix();
            }

            // D. Vertex Corner Handles in 3D
            Vector3[] handles = s_SceneCache.vertexHandles;
            if (handles != null && handles.Length > 0)
            {
                Handles.zTest = CompareFunction.LessEqual;
                Handles.color = new Color(1.0f, 0.95f, 0.40f, 1.0f);
                for (int i = 0; i < handles.Length; i++)
                {
                    Vector3 worldV = transform.TransformPoint(handles[i]);
                    float dotSize = HandleUtility.GetHandleSize(worldV) * 0.032f;
                    Handles.DotHandleCap(0, worldV, Quaternion.identity, dotSize, EventType.Repaint);
                }
            }

            // E. Centroid and SceneView HUD Tag (UV coordinates readout in 3D)
            if (!string.IsNullOrEmpty(s_SceneCache.badgeTitle))
            {
                Vector3 worldCentroid = transform.TransformPoint(s_SceneCache.localCentroid);
                Vector3 worldNormal = transform.TransformDirection(s_SceneCache.localNormal).normalized;
                Vector3 badgePos = worldCentroid + worldNormal * (HandleUtility.GetHandleSize(worldCentroid) * 0.08f);

                Camera cam = Camera.current;
                if (cam != null && Vector3.Dot(cam.transform.forward, worldCentroid - cam.transform.position) > 0)
                {
                    Handles.BeginGUI();
                    Vector2 screenPos = HandleUtility.WorldToGUIPoint(badgePos);

                    float badgeWidth = 210f;
                    float badgeHeight = 38f;
                    Rect badgeRect = new Rect(screenPos.x - badgeWidth * 0.5f, screenPos.y - badgeHeight - 10f, badgeWidth, badgeHeight);

                    EditorGUI.DrawRect(badgeRect, new Color(0.10f, 0.12f, 0.16f, 0.90f));
                    Color borderCol = new Color(1.0f, 0.75f, 0.18f, 0.90f);
                    EditorGUI.DrawRect(new Rect(badgeRect.x, badgeRect.y, badgeRect.width, 1), borderCol);
                    EditorGUI.DrawRect(new Rect(badgeRect.x, badgeRect.yMax - 1, badgeRect.width, 1), borderCol);
                    EditorGUI.DrawRect(new Rect(badgeRect.x, badgeRect.y, 1, badgeRect.height), borderCol);
                    EditorGUI.DrawRect(new Rect(badgeRect.xMax - 1, badgeRect.y, 1, badgeRect.height), borderCol);

                    if (s_SceneTitleStyle == null)
                    {
                        s_SceneTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                        {
                            fontSize = 11,
                            alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = new Color(1.0f, 0.85f, 0.25f, 1f) }
                        };
                    }
                    if (s_SceneUVStyle == null)
                    {
                        s_SceneUVStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            fontSize = 10,
                            alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = new Color(0.35f, 0.92f, 1.0f, 1f) }
                        };
                    }

                    GUI.Label(new Rect(badgeRect.x + 2, badgeRect.y + 2, badgeRect.width - 4, 17), s_SceneCache.badgeTitle, s_SceneTitleStyle);
                    GUI.Label(new Rect(badgeRect.x + 2, badgeRect.y + 18, badgeRect.width - 4, 17), s_SceneCache.badgeUV, s_SceneUVStyle);

                    // Vertex UV labels on individual corners
                    if (s_SceneCache.cornerChips != null && s_SceneCache.cornerChips.Length > 0)
                    {
                        if (s_SceneCornerStyle == null)
                        {
                            s_SceneCornerStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                fontSize = 9,
                                alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = new Color(0.95f, 0.95f, 0.95f, 1f) }
                            };
                        }

                        for (int i = 0; i < s_SceneCache.cornerChips.Length; i++)
                        {
                            var chip = s_SceneCache.cornerChips[i];
                            Vector3 wVert = transform.TransformPoint(chip.localPos);
                            Vector2 cScreen = HandleUtility.WorldToGUIPoint(wVert);
                            float cW = 75f;
                            Rect cRect = new Rect(cScreen.x - cW * 0.5f, cScreen.y + 6f, cW, 16f);
                            EditorGUI.DrawRect(cRect, new Color(0.08f, 0.08f, 0.10f, 0.80f));
                            GUI.Label(cRect, chip.uvText, s_SceneCornerStyle);
                        }
                    }

                    Handles.EndGUI();
                }
            }
        }

        #endregion

        #region UV Fixing & Transformation Operations

        public static Vector2[] FlipU(Vector2[] uvs, bool aroundCenter = true, HashSet<int> selectedIndices = null)
        {
            if (uvs == null) return Array.Empty<Vector2>();
            Vector2[] result = (Vector2[])uvs.Clone();

            float pivotX = 0.5f;
            if (!aroundCenter)
            {
                CalculateUVBounds(uvs, selectedIndices, out Vector2 min, out Vector2 max);
                pivotX = (min.x + max.x) * 0.5f;
            }

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (hasSelection)
            {
                foreach (int i in selectedIndices)
                {
                    if (i >= 0 && i < uvs.Length)
                    {
                        result[i] = new Vector2(2f * pivotX - uvs[i].x, uvs[i].y);
                    }
                }
            }
            else
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    result[i] = new Vector2(2f * pivotX - uvs[i].x, uvs[i].y);
                }
            }
            return result;
        }

        public static Vector2[] FlipV(Vector2[] uvs, bool aroundCenter = true, HashSet<int> selectedIndices = null)
        {
            if (uvs == null) return Array.Empty<Vector2>();
            Vector2[] result = (Vector2[])uvs.Clone();

            float pivotY = 0.5f;
            if (!aroundCenter)
            {
                CalculateUVBounds(uvs, selectedIndices, out Vector2 min, out Vector2 max);
                pivotY = (min.y + max.y) * 0.5f;
            }

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (hasSelection)
            {
                foreach (int i in selectedIndices)
                {
                    if (i >= 0 && i < uvs.Length)
                    {
                        result[i] = new Vector2(uvs[i].x, 2f * pivotY - uvs[i].y);
                    }
                }
            }
            else
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    result[i] = new Vector2(uvs[i].x, 2f * pivotY - uvs[i].y);
                }
            }
            return result;
        }

        public static Vector2[] FlipSelectedUVOrigin(
            Vector2[] uvs,
            UVFlipAxis axis = UVFlipAxis.Vertical,
            HashSet<int> selectedIndices = null,
            int[] triangles = null,
            bool perIsland = true)
        {
            if (uvs == null) return Array.Empty<Vector2>();
            Vector2[] result = (Vector2[])uvs.Clone();

            bool flipU = (axis == UVFlipAxis.Horizontal || axis == UVFlipAxis.Both);
            bool flipV = (axis == UVFlipAxis.Vertical || axis == UVFlipAxis.Both);

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (!hasSelection)
            {
                CalculateUVBounds(uvs, (HashSet<int>)null, out Vector2 min, out Vector2 max);
                Vector2 center = (min + max) * 0.5f;
                for (int i = 0; i < uvs.Length; i++)
                {
                    float x = flipU ? (2f * center.x - uvs[i].x) : uvs[i].x;
                    float y = flipV ? (2f * center.y - uvs[i].y) : uvs[i].y;
                    result[i] = new Vector2(x, y);
                }
                return result;
            }

            // Has selection: group by connected island if triangles are provided and perIsland is enabled
            List<HashSet<int>> groups = null;
            if (perIsland && triangles != null && triangles.Length >= 3)
            {
                groups = GroupSelectedVerticesByIsland(uvs, triangles, selectedIndices);
            }

            if (groups != null && groups.Count > 0)
            {
                foreach (var group in groups)
                {
                    if (group == null || group.Count == 0) continue;
                    CalculateUVBounds(uvs, group, out Vector2 gMin, out Vector2 gMax);
                    Vector2 gCenter = (gMin + gMax) * 0.5f;

                    foreach (int i in group)
                    {
                        if (i >= 0 && i < uvs.Length)
                        {
                            float x = flipU ? (2f * gCenter.x - uvs[i].x) : uvs[i].x;
                            float y = flipV ? (2f * gCenter.y - uvs[i].y) : uvs[i].y;
                            result[i] = new Vector2(x, y);
                        }
                    }
                }
            }
            else
            {
                CalculateUVBounds(uvs, selectedIndices, out Vector2 min, out Vector2 max);
                Vector2 center = (min + max) * 0.5f;

                foreach (int i in selectedIndices)
                {
                    if (i >= 0 && i < uvs.Length)
                    {
                        float x = flipU ? (2f * center.x - uvs[i].x) : uvs[i].x;
                        float y = flipV ? (2f * center.y - uvs[i].y) : uvs[i].y;
                        result[i] = new Vector2(x, y);
                    }
                }
            }

            return result;
        }

        public static List<HashSet<int>> GroupSelectedVerticesByIsland(Vector2[] uvs, int[] triangles, HashSet<int> selectedIndices)
        {
            if (uvs == null || triangles == null || selectedIndices == null || selectedIndices.Count == 0)
            {
                return new List<HashSet<int>>();
            }

            Dictionary<int, List<int>> adj = new Dictionary<int, List<int>>();
            foreach (int v in selectedIndices)
            {
                adj[v] = new List<int>();
            }

            int triCount = triangles.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                int i0 = triangles[t * 3];
                int i1 = triangles[t * 3 + 1];
                int i2 = triangles[t * 3 + 2];

                bool s0 = selectedIndices.Contains(i0);
                bool s1 = selectedIndices.Contains(i1);
                bool s2 = selectedIndices.Contains(i2);

                if (s0 && s1) { adj[i0].Add(i1); adj[i1].Add(i0); }
                if (s1 && s2) { adj[i1].Add(i2); adj[i2].Add(i1); }
                if (s2 && s0) { adj[i2].Add(i0); adj[i0].Add(i2); }
            }

            List<HashSet<int>> groups = new List<HashSet<int>>();
            HashSet<int> visited = new HashSet<int>();

            foreach (int v in selectedIndices)
            {
                if (visited.Contains(v)) continue;

                HashSet<int> currentGroup = new HashSet<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(v);
                visited.Add(v);
                currentGroup.Add(v);

                while (queue.Count > 0)
                {
                    int curr = queue.Dequeue();
                    if (adj.TryGetValue(curr, out var neighbors))
                    {
                        foreach (int n in neighbors)
                        {
                            if (visited.Add(n))
                            {
                                currentGroup.Add(n);
                                queue.Enqueue(n);
                            }
                        }
                    }
                }

                groups.Add(currentGroup);
            }

            return groups;
        }

        public static Vector2[] Rotate(Vector2[] uvs, float angleDegrees, Vector2 pivot, HashSet<int> selectedIndices = null)
        {
            if (uvs == null) return Array.Empty<Vector2>();
            Vector2[] result = (Vector2[])uvs.Clone();

            float rad = angleDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (hasSelection)
            {
                foreach (int i in selectedIndices)
                {
                    if (i >= 0 && i < uvs.Length)
                    {
                        float dx = uvs[i].x - pivot.x;
                        float dy = uvs[i].y - pivot.y;
                        float nx = pivot.x + (dx * cos - dy * sin);
                        float ny = pivot.y + (dx * sin + dy * cos);
                        result[i] = new Vector2(nx, ny);
                    }
                }
            }
            else
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    float dx = uvs[i].x - pivot.x;
                    float dy = uvs[i].y - pivot.y;
                    float nx = pivot.x + (dx * cos - dy * sin);
                    float ny = pivot.y + (dx * sin + dy * cos);
                    result[i] = new Vector2(nx, ny);
                }
            }
            return result;
        }

        public static Vector2[] Rotate90(Vector2[] uvs, bool clockwise, Vector2 pivot, HashSet<int> selectedIndices = null)
        {
            return Rotate(uvs, clockwise ? -90f : 90f, pivot, selectedIndices);
        }

        public static Vector2[] TransformUVs(Vector2[] uvs, Vector2 offset, Vector2 scale, float rotationDegrees, Vector2 pivot, HashSet<int> selectedIndices = null)
        {
            if (uvs == null) return Array.Empty<Vector2>();
            Vector2[] result = (Vector2[])uvs.Clone();

            float rad = rotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (hasSelection)
            {
                foreach (int i in selectedIndices)
                {
                    if (i >= 0 && i < uvs.Length)
                    {
                        // 1. Pivot translation & Scale
                        float dx = (uvs[i].x - pivot.x) * scale.x;
                        float dy = (uvs[i].y - pivot.y) * scale.y;

                        // 2. Rotate
                        float nx = (dx * cos - dy * sin) + pivot.x;
                        float ny = (dx * sin + dy * cos) + pivot.y;

                        // 3. Offset
                        result[i] = new Vector2(nx + offset.x, ny + offset.y);
                    }
                }
            }
            else
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    // 1. Pivot translation & Scale
                    float dx = (uvs[i].x - pivot.x) * scale.x;
                    float dy = (uvs[i].y - pivot.y) * scale.y;

                    // 2. Rotate
                    float nx = (dx * cos - dy * sin) + pivot.x;
                    float ny = (dx * sin + dy * cos) + pivot.y;

                    // 3. Offset
                    result[i] = new Vector2(nx + offset.x, ny + offset.y);
                }
            }
            return result;
        }

        public static Vector2[] NormalizeUVs(Vector2[] uvs, HashSet<int> selectedIndices = null)
        {
            if (uvs == null || uvs.Length == 0) return Array.Empty<Vector2>();
            Vector2[] result = (Vector2[])uvs.Clone();

            CalculateUVBounds(uvs, selectedIndices, out Vector2 min, out Vector2 max);
            float w = max.x - min.x;
            float h = max.y - min.y;

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (hasSelection)
            {
                foreach (int i in selectedIndices)
                {
                    if (i >= 0 && i < uvs.Length)
                    {
                        float u = w > 1e-6f ? (uvs[i].x - min.x) / w : 0.5f;
                        float v = h > 1e-6f ? (uvs[i].y - min.y) / h : 0.5f;
                        result[i] = new Vector2(u, v);
                    }
                }
            }
            else
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    float u = w > 1e-6f ? (uvs[i].x - min.x) / w : 0.5f;
                    float v = h > 1e-6f ? (uvs[i].y - min.y) / h : 0.5f;
                    result[i] = new Vector2(u, v);
                }
            }
            return result;
        }

        public static Vector2[] FitPreserveAspect(Vector2[] uvs, HashSet<int> selectedIndices = null)
        {
            if (uvs == null || uvs.Length == 0) return Array.Empty<Vector2>();
            Vector2[] result = (Vector2[])uvs.Clone();

            CalculateUVBounds(uvs, selectedIndices, out Vector2 min, out Vector2 max);
            float w = max.x - min.x;
            float h = max.y - min.y;
            float maxDim = Mathf.Max(w, h);

            if (maxDim < 1e-6f) return result;

            float offsetX = (1f - (w / maxDim)) * 0.5f;
            float offsetY = (1f - (h / maxDim)) * 0.5f;

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (hasSelection)
            {
                foreach (int i in selectedIndices)
                {
                    if (i >= 0 && i < uvs.Length)
                    {
                        float u = ((uvs[i].x - min.x) / maxDim) + offsetX;
                        float v = ((uvs[i].y - min.y) / maxDim) + offsetY;
                        result[i] = new Vector2(u, v);
                    }
                }
            }
            else
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    float u = ((uvs[i].x - min.x) / maxDim) + offsetX;
                    float v = ((uvs[i].y - min.y) / maxDim) + offsetY;
                    result[i] = new Vector2(u, v);
                }
            }
            return result;
        }

        public static Vector2[] CenterUVs(Vector2[] uvs, HashSet<int> selectedIndices = null)
        {
            if (uvs == null || uvs.Length == 0) return Array.Empty<Vector2>();
            Vector2[] result = (Vector2[])uvs.Clone();

            CalculateUVBounds(uvs, selectedIndices, out Vector2 min, out Vector2 max);
            Vector2 center = (min + max) * 0.5f;
            Vector2 shift = new Vector2(0.5f, 0.5f) - center;

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (hasSelection)
            {
                foreach (int i in selectedIndices)
                {
                    if (i >= 0 && i < uvs.Length)
                    {
                        result[i] = uvs[i] + shift;
                    }
                }
            }
            else
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    result[i] = uvs[i] + shift;
                }
            }
            return result;
        }

        public static Vector2[] Clamp01(Vector2[] uvs, HashSet<int> selectedIndices = null)
        {
            if (uvs == null) return Array.Empty<Vector2>();
            Vector2[] result = (Vector2[])uvs.Clone();

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (hasSelection)
            {
                foreach (int i in selectedIndices)
                {
                    if (i >= 0 && i < uvs.Length)
                    {
                        result[i] = new Vector2(Mathf.Clamp01(uvs[i].x), Mathf.Clamp01(uvs[i].y));
                    }
                }
            }
            else
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    result[i] = new Vector2(Mathf.Clamp01(uvs[i].x), Mathf.Clamp01(uvs[i].y));
                }
            }
            return result;
        }

        public static Vector2[] Wrap01(Vector2[] uvs, HashSet<int> selectedIndices = null)
        {
            if (uvs == null) return Array.Empty<Vector2>();
            Vector2[] result = (Vector2[])uvs.Clone();

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (hasSelection)
            {
                foreach (int i in selectedIndices)
                {
                    if (i >= 0 && i < uvs.Length)
                    {
                        float u = uvs[i].x - Mathf.Floor(uvs[i].x);
                        float v = uvs[i].y - Mathf.Floor(uvs[i].y);
                        result[i] = new Vector2(u, v);
                    }
                }
            }
            else
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    float u = uvs[i].x - Mathf.Floor(uvs[i].x);
                    float v = uvs[i].y - Mathf.Floor(uvs[i].y);
                    result[i] = new Vector2(u, v);
                }
            }
            return result;
        }

        #endregion

        #region Projections & Auto Unwrapping

        public static Vector2[] ProjectBox(Mesh mesh, Vector2 scale, Vector2 offset)
        {
            if (mesh == null) return Array.Empty<Vector2>();

            Vector3[] verts = mesh.vertices;
            Vector3[] norms = mesh.normals;
            if (norms == null || norms.Length != verts.Length)
            {
                mesh.RecalculateNormals();
                norms = mesh.normals;
            }

            Vector2[] result = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                Vector3 n = norms[i];
                float ax = Mathf.Abs(n.x);
                float ay = Mathf.Abs(n.y);
                float az = Mathf.Abs(n.z);

                Vector2 uv;
                if (ay >= ax && ay >= az)
                {
                    // Top / Bottom plane (XZ)
                    uv = new Vector2(v.x, v.z);
                    if (n.y < 0) uv.x = -uv.x;
                }
                else if (ax >= ay && ax >= az)
                {
                    // Left / Right plane (YZ)
                    uv = new Vector2(v.z, v.y);
                    if (n.x > 0) uv.x = -uv.x;
                }
                else
                {
                    // Front / Back plane (XY)
                    uv = new Vector2(v.x, v.y);
                    if (n.z < 0) uv.x = -uv.x;
                }

                result[i] = new Vector2(uv.x * scale.x + offset.x, uv.y * scale.y + offset.y);
            }
            return result;
        }

        public enum PlanarAxis { XY_Front, XZ_Top, YZ_Right }

        public static Vector2[] ProjectPlanar(Mesh mesh, PlanarAxis axis, Vector2 scale, Vector2 offset, bool normalize)
        {
            if (mesh == null) return Array.Empty<Vector2>();
            Vector3[] verts = mesh.vertices;
            Vector2[] result = new Vector2[verts.Length];

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                Vector2 uv = axis switch
                {
                    PlanarAxis.XZ_Top => new Vector2(v.x, v.z),
                    PlanarAxis.YZ_Right => new Vector2(v.z, v.y),
                    _ => new Vector2(v.x, v.y)
                };

                result[i] = new Vector2(uv.x * scale.x + offset.x, uv.y * scale.y + offset.y);
            }

            if (normalize)
            {
                result = NormalizeUVs(result);
            }

            return result;
        }

        public static Vector2[] ProjectCylindrical(Mesh mesh, Vector2 scale, Vector2 offset)
        {
            if (mesh == null) return Array.Empty<Vector2>();
            Vector3[] verts = mesh.vertices;
            Vector2[] result = new Vector2[verts.Length];

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                float angle = Mathf.Atan2(v.z, v.x); // -PI to PI
                float u = (angle / (2f * Mathf.PI)) + 0.5f;
                float vCoord = v.y;
                result[i] = new Vector2(u * scale.x + offset.x, vCoord * scale.y + offset.y);
            }
            return result;
        }

        public static Vector2[] ProjectSpherical(Mesh mesh, Vector2 scale, Vector2 offset)
        {
            if (mesh == null) return Array.Empty<Vector2>();
            Vector3[] verts = mesh.vertices;
            Vector2[] result = new Vector2[verts.Length];

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 d = verts[i];
                if (d.sqrMagnitude > 1e-6f) d.Normalize();
                else d = Vector3.up;

                float u = 0.5f + Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI);
                float v = 0.5f - Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI;
                result[i] = new Vector2(u * scale.x + offset.x, v * scale.y + offset.y);
            }
            return result;
        }

        public static bool GenerateLightmapUVs(Mesh mesh, float hardAngle = 88f, float packMargin = 4f, float angleError = 8f, float areaError = 15f)
        {
            if (mesh == null) return false;

            UnwrapParam param;
            UnwrapParam.SetDefaults(out param);
            param.hardAngle = hardAngle;
            param.packMargin = packMargin / 1024f;
            param.angleError = angleError / 100f;
            param.areaError = areaError / 100f;

            Unwrapping.GenerateSecondaryUVSet(mesh, param);
            InvalidateCache();
            return true;
        }

        #endregion

        #region Channel Operations

        public static void CopyChannel(Mesh mesh, int srcChannel, int dstChannel)
        {
            if (mesh == null || srcChannel == dstChannel) return;
            Vector2[] src = GetUVs(mesh, srcChannel);
            if (src.Length == 0) return;
            SetUVs(mesh, dstChannel, src);
        }

        public static void SwapChannels(Mesh mesh, int channelA, int channelB)
        {
            if (mesh == null || channelA == channelB) return;
            Vector2[] uvsA = GetUVs(mesh, channelA);
            Vector2[] uvsB = GetUVs(mesh, channelB);
            SetUVs(mesh, channelA, uvsB);
            SetUVs(mesh, channelB, uvsA);
        }

        public static void ClearChannel(Mesh mesh, int channel)
        {
            if (mesh == null) return;
            mesh.SetUVs(channel, (List<Vector2>)null);
            mesh.UploadMeshData(false);
            InvalidateCache();
        }

        #endregion

        #region Asset Management & Export

        public static Mesh EnsureMeshInstance(UnityEngine.Object owner, Mesh currentMesh, Action<Mesh> setMeshCallback)
        {
            if (currentMesh == null) return null;

            // If the mesh is an asset on disk (e.g. FBX model sub-asset), instantiate a scene copy
            if (AssetDatabase.Contains(currentMesh))
            {
                if (owner != null)
                {
                    Undo.RegisterCompleteObjectUndo(owner, "Instantiate Mesh For UV Edit");
                }
                Mesh instance = UnityEngine.Object.Instantiate(currentMesh);
                instance.name = currentMesh.name + " (UV Edited)";
                setMeshCallback(instance);
                if (owner != null)
                {
                    EditorUtility.SetDirty(owner);
                }
                InvalidateCache();
                return instance;
            }

            return currentMesh;
        }

        public static Mesh SaveAsNewAsset(Mesh mesh, string defaultName)
        {
            if (mesh == null) return null;

            string safeName = string.IsNullOrEmpty(defaultName) ? "Mesh_FixedUV" : defaultName.Replace(" (UV Edited)", "") + "_FixedUV";
            string path = EditorUtility.SaveFilePanelInProject("Save Modified Mesh Asset", safeName, "asset", "Select location to save mesh asset.");

            if (string.IsNullOrEmpty(path)) return null;

            Mesh clone = UnityEngine.Object.Instantiate(mesh);
            clone.name = Path.GetFileNameWithoutExtension(path);

            AssetDatabase.CreateAsset(clone, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Mesh Saved", $"Mesh successfully saved to:\n{path}", "OK");
            return clone;
        }

        public static bool OverwriteAsset(Mesh mesh)
        {
            if (mesh == null) return false;

            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("Cannot Overwrite", "This mesh is an in-memory scene instance and has not been saved as a project .asset file yet. Use 'Save As New Asset...' first.", "OK");
                return false;
            }

            if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Model Asset Protected", "Overwriting original imported FBX/OBJ 3D models directly is prohibited to protect source files. Please use 'Save As New Asset...' to create a native Unity .asset mesh.", "OK");
                return false;
            }

            if (EditorUtility.DisplayDialog("Overwrite Asset", $"Are you sure you want to overwrite mesh asset at:\n{path}?", "Overwrite", "Cancel"))
            {
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return true;
            }
            return false;
        }

        public static void ExportUVLayoutToPNG(Mesh mesh, int channel, int resolution, Color wireColor, Color bgColor, bool transparent, int submesh = -1)
        {
            if (mesh == null) return;

            Vector2[] uvs = GetUVs(mesh, channel);
            if (uvs.Length == 0)
            {
                EditorUtility.DisplayDialog("Export Failed", $"UV channel {channel} is empty.", "OK");
                return;
            }

            string defaultName = mesh.name.Replace(" (UV Edited)", "") + $"_UV{channel}_Layout.png";
            string path = EditorUtility.SaveFilePanel("Export UV Layout as PNG", "", defaultName, "png");
            if (string.IsNullOrEmpty(path)) return;

            RenderTexture exportRT = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 8
            };
            exportRT.Create();

            RenderTexture prevRT = RenderTexture.active;
            RenderTexture.active = exportRT;

            Color clearCol = transparent ? new Color(0, 0, 0, 0) : bgColor;
            GL.Clear(true, true, clearCol);

            Matrix4x4 proj = Matrix4x4.Ortho(0, 1, 0, 1, -1f, 1f);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(proj);
            GL.modelview = Matrix4x4.identity;

            Material mat = GetPreviewMaterial();
            if (mat != null)
            {
                mat.SetPass(0);
                int[] triangles = GetTriangles(mesh, submesh);

                GL.Begin(GL.LINES);
                GL.Color(wireColor);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int i0 = triangles[i];
                    int i1 = triangles[i + 1];
                    int i2 = triangles[i + 2];

                    if (i0 < uvs.Length && i1 < uvs.Length && i2 < uvs.Length)
                    {
                        Vector2 p0 = uvs[i0];
                        Vector2 p1 = uvs[i1];
                        Vector2 p2 = uvs[i2];

                        GL.Vertex3(p0.x, p0.y, 0); GL.Vertex3(p1.x, p1.y, 0);
                        GL.Vertex3(p1.x, p1.y, 0); GL.Vertex3(p2.x, p2.y, 0);
                        GL.Vertex3(p2.x, p2.y, 0); GL.Vertex3(p0.x, p0.y, 0);
                    }
                }
                GL.End();
            }

            GL.PopMatrix();

            Texture2D exportTex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            exportTex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            exportTex.Apply();

            RenderTexture.active = prevRT;
            exportRT.Release();
            UnityEngine.Object.DestroyImmediate(exportRT);

            byte[] pngBytes = exportTex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(exportTex);

            File.WriteAllBytes(path, pngBytes);

            if (path.StartsWith(Application.dataPath))
            {
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog("Export Successful", $"UV Layout exported successfully to:\n{path}", "OK");
        }

        #endregion

        #region 3D Mesh Vertex Transformations (XYZ)

        public static void Transform3DVertices(
            Mesh mesh,
            Vector3 positionOffset,
            Vector3 rotationEuler,
            Vector3 scale,
            int pivotMode = 0, // 0 = Centroid, 1 = Origin (0,0,0), 2 = Bounds Center
            HashSet<int> selectedIndices = null)
        {
            if (mesh == null) return;

            Vector3[] verts = mesh.vertices;
            if (verts == null || verts.Length == 0) return;

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;

            // Calculate 3D Pivot
            Vector3 pivot = Vector3.zero;
            if (pivotMode == 0) // Centroid
            {
                Vector3 sum = Vector3.zero;
                int count = 0;
                if (hasSelection)
                {
                    foreach (int idx in selectedIndices)
                    {
                        if (idx >= 0 && idx < verts.Length)
                        {
                            sum += verts[idx];
                            count++;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < verts.Length; i++) sum += verts[i];
                    count = verts.Length;
                }
                pivot = count > 0 ? sum / count : Vector3.zero;
            }
            else if (pivotMode == 2) // Bounds Center
            {
                Bounds b = mesh.bounds;
                pivot = b.center;
            }

            Quaternion rot = Quaternion.Euler(rotationEuler);

            if (hasSelection)
            {
                foreach (int idx in selectedIndices)
                {
                    if (idx >= 0 && idx < verts.Length)
                    {
                        Vector3 p = verts[idx] - pivot;
                        p = new Vector3(p.x * scale.x, p.y * scale.y, p.z * scale.z);
                        p = rot * p;
                        verts[idx] = p + pivot + positionOffset;
                    }
                }
            }
            else
            {
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 p = verts[i] - pivot;
                    p = new Vector3(p.x * scale.x, p.y * scale.y, p.z * scale.z);
                    p = rot * p;
                    verts[i] = p + pivot + positionOffset;
                }
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
        }

        public static void CenterMeshToOrigin(Mesh mesh, HashSet<int> selectedIndices = null)
        {
            if (mesh == null) return;
            Vector3[] verts = mesh.vertices;
            if (verts == null || verts.Length == 0) return;

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            Vector3 center = Vector3.zero;
            int count = 0;

            if (hasSelection)
            {
                foreach (int idx in selectedIndices)
                {
                    if (idx >= 0 && idx < verts.Length)
                    {
                        center += verts[idx];
                        count++;
                    }
                }
            }
            else
            {
                for (int i = 0; i < verts.Length; i++) center += verts[i];
                count = verts.Length;
            }

            if (count == 0) return;
            center /= count;

            Vector3 offset = -center;
            if (hasSelection)
            {
                foreach (int idx in selectedIndices)
                {
                    if (idx >= 0 && idx < verts.Length) verts[idx] += offset;
                }
            }
            else
            {
                for (int i = 0; i < verts.Length; i++) verts[i] += offset;
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
        }

        public static void MirrorMeshX(Mesh mesh, HashSet<int> selectedIndices = null)
        {
            if (mesh == null) return;
            Vector3[] verts = mesh.vertices;
            if (verts == null || verts.Length == 0) return;

            bool hasSelection = selectedIndices != null && selectedIndices.Count > 0;
            if (hasSelection)
            {
                foreach (int idx in selectedIndices)
                {
                    if (idx >= 0 && idx < verts.Length) verts[idx] = new Vector3(-verts[idx].x, verts[idx].y, verts[idx].z);
                }
            }
            else
            {
                for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(-verts[i].x, verts[i].y, verts[i].z);
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
        }

        #endregion

        #region UV History & Undo/Redo

        public class UVHistoryStep
        {
            public Vector2[] uvs;
            public string actionName;
            public HashSet<int> selectedVertices;

            public UVHistoryStep(Vector2[] uvs, string actionName, HashSet<int> selectedVertices = null)
            {
                this.uvs = uvs != null ? (Vector2[])uvs.Clone() : Array.Empty<Vector2>();
                this.actionName = string.IsNullOrEmpty(actionName) ? "UV Edit" : actionName;
                this.selectedVertices = selectedVertices != null ? new HashSet<int>(selectedVertices) : null;
            }
        }

        public class MeshUVHistory
        {
            private readonly List<UVHistoryStep> m_Steps = new List<UVHistoryStep>();
            private int m_CurrentIndex = -1;
            private const int MaxSteps = 50;

            public bool CanUndo => m_CurrentIndex > 0;
            public bool CanRedo => m_CurrentIndex >= 0 && m_CurrentIndex < m_Steps.Count - 1;

            public string CurrentUndoName => CanUndo ? m_Steps[m_CurrentIndex].actionName : string.Empty;
            public string CurrentRedoName => CanRedo ? m_Steps[m_CurrentIndex + 1].actionName : string.Empty;

            public void EnsureInitialState(Vector2[] currentUVs, string baseName = "Initial State", HashSet<int> selectedVertices = null)
            {
                if (m_Steps.Count == 0 && currentUVs != null && currentUVs.Length > 0)
                {
                    m_Steps.Add(new UVHistoryStep(currentUVs, baseName, selectedVertices));
                    m_CurrentIndex = 0;
                }
            }

            public void RecordStep(Vector2[] newUVs, string actionName, HashSet<int> selectedVertices = null)
            {
                if (newUVs == null || newUVs.Length == 0) return;

                if (m_Steps.Count == 0)
                {
                    m_Steps.Add(new UVHistoryStep(newUVs, "Initial State", selectedVertices));
                    m_CurrentIndex = 0;
                    return;
                }

                if (m_CurrentIndex < m_Steps.Count - 1)
                {
                    m_Steps.RemoveRange(m_CurrentIndex + 1, m_Steps.Count - (m_CurrentIndex + 1));
                }

                m_Steps.Add(new UVHistoryStep(newUVs, actionName, selectedVertices));
                m_CurrentIndex = m_Steps.Count - 1;

                if (m_Steps.Count > MaxSteps)
                {
                    m_Steps.RemoveAt(0);
                    m_CurrentIndex--;
                }
            }

            public UVHistoryStep Undo()
            {
                if (!CanUndo) return null;
                m_CurrentIndex--;
                return m_Steps[m_CurrentIndex];
            }

            public UVHistoryStep Redo()
            {
                if (!CanRedo) return null;
                m_CurrentIndex++;
                return m_Steps[m_CurrentIndex];
            }

            public void Clear()
            {
                m_Steps.Clear();
                m_CurrentIndex = -1;
            }
        }

        public static class UVHistory
        {
            private static readonly Dictionary<long, MeshUVHistory> s_Histories = new Dictionary<long, MeshUVHistory>();

            private static long MakeKey(Mesh mesh, int channel)
            {
                if (mesh == null) return 0;
                return ((long)mesh.GetInstanceID() << 32) | (uint)(channel & 0xFFFFFFFF);
            }

            public static MeshUVHistory GetOrCreateHistory(Mesh mesh, int channel)
            {
                if (mesh == null) return null;
                long key = MakeKey(mesh, channel);
                if (!s_Histories.TryGetValue(key, out var hist))
                {
                    hist = new MeshUVHistory();
                    s_Histories[key] = hist;
                }
                return hist;
            }

            public static void EnsureInitialState(Mesh mesh, int channel, Vector2[] currentUVs, string baseName = "Initial State", HashSet<int> selectedVertices = null)
            {
                if (mesh == null) return;
                var hist = GetOrCreateHistory(mesh, channel);
                hist?.EnsureInitialState(currentUVs, baseName, selectedVertices);
            }

            public static void RecordStep(Mesh mesh, int channel, Vector2[] newUVs, string actionName, HashSet<int> selectedVertices = null)
            {
                if (mesh == null) return;
                var hist = GetOrCreateHistory(mesh, channel);
                hist?.RecordStep(newUVs, actionName, selectedVertices);
            }

            public static bool CanUndo(Mesh mesh, int channel)
            {
                if (mesh == null) return false;
                var hist = GetOrCreateHistory(mesh, channel);
                return hist != null && hist.CanUndo;
            }

            public static bool CanRedo(Mesh mesh, int channel)
            {
                if (mesh == null) return false;
                var hist = GetOrCreateHistory(mesh, channel);
                return hist != null && hist.CanRedo;
            }

            public static string GetUndoName(Mesh mesh, int channel)
            {
                if (mesh == null) return string.Empty;
                var hist = GetOrCreateHistory(mesh, channel);
                return hist != null ? hist.CurrentUndoName : string.Empty;
            }

            public static string GetRedoName(Mesh mesh, int channel)
            {
                if (mesh == null) return string.Empty;
                var hist = GetOrCreateHistory(mesh, channel);
                return hist != null ? hist.CurrentRedoName : string.Empty;
            }

            public static bool PerformUndo(Mesh mesh, int channel, HashSet<int> selectedVertices = null, Action onUVsChanged = null)
            {
                if (mesh == null) return false;
                var hist = GetOrCreateHistory(mesh, channel);
                if (hist == null || !hist.CanUndo) return false;

                var step = hist.Undo();
                if (step == null || step.uvs == null) return false;

                SetUVs(mesh, channel, step.uvs);
                if (selectedVertices != null && step.selectedVertices != null)
                {
                    selectedVertices.Clear();
                    selectedVertices.UnionWith(step.selectedVertices);
                }
                EditorUtility.SetDirty(mesh);
                InvalidateCache();
                onUVsChanged?.Invoke();
                return true;
            }

            public static bool PerformRedo(Mesh mesh, int channel, HashSet<int> selectedVertices = null, Action onUVsChanged = null)
            {
                if (mesh == null) return false;
                var hist = GetOrCreateHistory(mesh, channel);
                if (hist == null || !hist.CanRedo) return false;

                var step = hist.Redo();
                if (step == null || step.uvs == null) return false;

                SetUVs(mesh, channel, step.uvs);
                if (selectedVertices != null && step.selectedVertices != null)
                {
                    selectedVertices.Clear();
                    selectedVertices.UnionWith(step.selectedVertices);
                }
                EditorUtility.SetDirty(mesh);
                InvalidateCache();
                onUVsChanged?.Invoke();
                return true;
            }

            public static void ClearHistory(Mesh mesh, int channel = -1)
            {
                if (mesh == null) return;
                if (channel >= 0)
                {
                    s_Histories.Remove(MakeKey(mesh, channel));
                }
                else
                {
                    for (int c = 0; c < 8; c++)
                    {
                        s_Histories.Remove(MakeKey(mesh, c));
                    }
                }
            }
        }

        public static bool IsUndoEvent(Event evt)
        {
            if (evt == null) return false;
            if (EditorGUIUtility.editingTextField) return false;

            if (evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand)
            {
                return evt.commandName == "Undo";
            }

            if (evt.type == EventType.KeyDown)
            {
                bool isCtrlOrCmd = (Application.platform == RuntimePlatform.OSXEditor) ? evt.command : evt.control;
                return isCtrlOrCmd && evt.keyCode == KeyCode.Z && !evt.shift && !evt.alt;
            }

            return false;
        }

        public static bool IsRedoEvent(Event evt)
        {
            if (evt == null) return false;
            if (EditorGUIUtility.editingTextField) return false;

            if (evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand)
            {
                return evt.commandName == "Redo";
            }

            if (evt.type == EventType.KeyDown)
            {
                bool isCtrlOrCmd = (Application.platform == RuntimePlatform.OSXEditor) ? evt.command : evt.control;
                bool isCtrlY = isCtrlOrCmd && evt.keyCode == KeyCode.Y && !evt.alt;
                bool isCtrlShiftZ = isCtrlOrCmd && evt.keyCode == KeyCode.Z && evt.shift && !evt.alt;
                return isCtrlY || isCtrlShiftZ;
            }

            return false;
        }

        public static bool HandleUndoRedoEvents(
            Event evt,
            bool isHovered,
            Mesh mesh,
            int channel,
            HashSet<int> selectedVertices,
            Action onUVsChanged)
        {
            if (evt == null || !isHovered || mesh == null) return false;
            if (evt.type == EventType.Used) return false;

            bool isUndo = IsUndoEvent(evt);
            bool isRedo = IsRedoEvent(evt);

            if (!isUndo && !isRedo) return false;

            if (evt.type == EventType.ValidateCommand)
            {
                if ((isUndo && UVHistory.CanUndo(mesh, channel)) ||
                    (isRedo && UVHistory.CanRedo(mesh, channel)))
                {
                    evt.Use();
                    return true;
                }
                return false;
            }

            if (isUndo)
            {
                if (UVHistory.PerformUndo(mesh, channel, selectedVertices, onUVsChanged))
                {
                    evt.Use();
                    GUI.changed = true;
                    return true;
                }
                else
                {
                    evt.Use();
                    return true;
                }
            }
            else if (isRedo)
            {
                if (UVHistory.PerformRedo(mesh, channel, selectedVertices, onUVsChanged))
                {
                    evt.Use();
                    GUI.changed = true;
                    return true;
                }
                else
                {
                    evt.Use();
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
#endif

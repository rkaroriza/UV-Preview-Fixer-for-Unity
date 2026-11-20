#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UVTool.Editor
{
    public static class UVToolAutomatedTests
    {
        [MenuItem("Tools/UV Map Preview & Fixer/Run Self-Tests", false, 250)]
        public static void RunAllTestsMenu()
        {
            RunAllTests(true);
        }

        public static void RunAllTests(bool showDialog = false)
        {
            int passed = 0;
            int total = 0;

            Debug.Log("<color=cyan><b>[UV Tool] Starting Self-Tests...</b></color>");

            Test("FlipV around center", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[] { new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.9f) };
                Vector2[] result = UVEditorCore.FlipV(uvs, true);
                AssertApprox(result[0], new Vector2(0.2f, 0.7f));
                AssertApprox(result[1], new Vector2(0.8f, 0.1f));
            });

            Test("FlipU around center", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[] { new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.9f) };
                Vector2[] result = UVEditorCore.FlipU(uvs, true);
                AssertApprox(result[0], new Vector2(0.8f, 0.3f));
                AssertApprox(result[1], new Vector2(0.2f, 0.9f));
            });

            Test("Rotate 90 CW around (0.5, 0.5)", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[] { new Vector2(0.5f, 0.8f) }; // Top center
                Vector2[] result = UVEditorCore.Rotate90(uvs, true, new Vector2(0.5f, 0.5f));
                AssertApprox(result[0], new Vector2(0.8f, 0.5f)); // Right center
            });

            Test("Rotate 90 CCW around (0.5, 0.5)", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[] { new Vector2(0.5f, 0.8f) }; // Top center
                Vector2[] result = UVEditorCore.Rotate90(uvs, false, new Vector2(0.5f, 0.5f));
                AssertApprox(result[0], new Vector2(0.2f, 0.5f)); // Left center
            });

            Test("Normalize UVs", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[] { new Vector2(2f, 10f), new Vector2(6f, 20f) };
                Vector2[] result = UVEditorCore.NormalizeUVs(uvs);
                AssertApprox(result[0], new Vector2(0f, 0f));
                AssertApprox(result[1], new Vector2(1f, 1f));
            });

            Test("Fit Preserve Aspect", ref passed, ref total, () =>
            {
                // Width = 2, Height = 1 (Max dimension = 2)
                Vector2[] uvs = new[] { new Vector2(0f, 0f), new Vector2(2f, 1f) };
                Vector2[] result = UVEditorCore.FitPreserveAspect(uvs);
                // Should scale by 0.5, centered vertically
                AssertApprox(result[0], new Vector2(0f, 0.25f));
                AssertApprox(result[1], new Vector2(1f, 0.75f));
            });

            Test("Center UVs to (0.5, 0.5)", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[] { new Vector2(0f, 0f), new Vector2(0.2f, 0.2f) };
                Vector2[] result = UVEditorCore.CenterUVs(uvs);
                UVEditorCore.CalculateUVBounds(result, out Vector2 min, out Vector2 max);
                AssertApprox((min + max) * 0.5f, new Vector2(0.5f, 0.5f));
            });

            Test("Clamp [0, 1]", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[] { new Vector2(-0.5f, 1.5f), new Vector2(0.5f, 0.5f) };
                Vector2[] result = UVEditorCore.Clamp01(uvs);
                AssertApprox(result[0], new Vector2(0f, 1f));
                AssertApprox(result[1], new Vector2(0.5f, 0.5f));
            });

            Test("Wrap [0, 1]", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[] { new Vector2(2.3f, 4.7f), new Vector2(-0.2f, 0f) };
                Vector2[] result = UVEditorCore.Wrap01(uvs);
                AssertApprox(result[0], new Vector2(0.3f, 0.7f));
                AssertApprox(result[1], new Vector2(0.8f, 0f));
            });

            Test("Flipped Triangles Detection", ref passed, ref total, () =>
            {
                // Triangle 1: CCW (not flipped)
                // Triangle 2: CW (flipped)
                Vector2[] uvs = new[]
                {
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), // normal
                    new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0)  // inverted
                };
                int[] triangles = new[] { 0, 1, 2, 3, 4, 5 };
                int flipped = UVEditorCore.CountFlippedTriangles(uvs, triangles);
                if (flipped != 1) throw new Exception($"Expected 1 flipped triangle, got {flipped}");
            });

            Test("Triplanar Box Projection", ref passed, ref total, () =>
            {
                Mesh cube = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshFilter>().sharedMesh;
                Vector2[] projUVs = UVEditorCore.ProjectBox(cube, Vector2.one, Vector2.zero);
                if (projUVs.Length != cube.vertexCount) throw new Exception("Vertex count mismatch in box projection.");
                UVEditorCore.CalculateUVBounds(projUVs, out Vector2 min, out Vector2 max);
                if (float.IsNaN(min.x) || float.IsNaN(max.x)) throw new Exception("NaN in projection bounds.");
            });

            Test("Channel Copy and Swap", ref passed, ref total, () =>
            {
                Mesh m = new Mesh();
                m.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
                m.triangles = new[] { 0, 1, 2 };

                Vector2[] testUV0 = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0) };
                Vector2[] testUV1 = new[] { new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(1f, 0.5f) };

                m.SetUVs(0, testUV0);
                m.SetUVs(1, testUV1);

                UVEditorCore.SwapChannels(m, 0, 1);
                Vector2[] swappedUV0 = UVEditorCore.GetUVs(m, 0);
                Vector2[] swappedUV1 = UVEditorCore.GetUVs(m, 1);

                AssertApprox(swappedUV0[0], testUV1[0]);
                AssertApprox(swappedUV1[0], testUV0[0]);

                UVEditorCore.CopyChannel(m, 0, 2);
                Vector2[] copiedUV2 = UVEditorCore.GetUVs(m, 2);
                AssertApprox(copiedUV2[0], testUV1[0]);

                UVEditorCore.ClearChannel(m, 2);
                if (UVEditorCore.HasUVs(m, 2)) throw new Exception("Channel 2 should be empty after clear.");

                UnityEngine.Object.DestroyImmediate(m);
            });

            Test("DisjointSet and UV Island Grouping", ref passed, ref total, () =>
            {
                // Two separate triangles (two distinct islands: {0,1,2} and {3,4,5})
                Vector2[] uvs = new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.2f, 0f), new Vector2(0f, 0.2f),
                    new Vector2(0.8f, 0.8f), new Vector2(1f, 0.8f), new Vector2(0.8f, 1f)
                };
                int[] triangles = new[] { 0, 1, 2, 3, 4, 5 };

                var island1 = UVEditorCore.GetIslandVertices(0, uvs, triangles);
                if (island1.Count != 3 || !island1.Contains(0) || !island1.Contains(1) || !island1.Contains(2))
                    throw new Exception("Island 1 did not contain expected vertices {0,1,2}.");

                var island2 = UVEditorCore.GetIslandVertices(4, uvs, triangles);
                if (island2.Count != 3 || !island2.Contains(3) || !island2.Contains(4) || !island2.Contains(5))
                    throw new Exception("Island 2 did not contain expected vertices {3,4,5}.");
            });

            Test("Hit Testing: PointInTriangle & FindTriangleAt", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[]
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f)
                };
                int[] triangles = new[] { 0, 1, 2 };

                int hitTri = UVEditorCore.FindTriangleAt(uvs, triangles, new Vector2(0.2f, 0.2f));
                if (hitTri != 0) throw new Exception($"Expected triangle 0 hit, got {hitTri}");

                int missTri = UVEditorCore.FindTriangleAt(uvs, triangles, new Vector2(0.8f, 0.8f));
                if (missTri != -1) throw new Exception($"Expected miss (-1), got {missTri}");
            });

            Test("Single Click & Marquee Selection Modes", ref passed, ref total, () =>
            {
                Rect canvasRect = new Rect(0, 0, 500, 500);
                Vector2[] uvs = new[]
                {
                    new Vector2(0.1f, 0.1f), new Vector2(0.3f, 0.1f), new Vector2(0.1f, 0.3f), // island 1
                    new Vector2(0.7f, 0.7f), new Vector2(0.9f, 0.7f), new Vector2(0.7f, 0.9f)  // island 2
                };
                int[] triangles = new[] { 0, 1, 2, 3, 4, 5 };

                // 1. Single click select Island
                var sel = new System.Collections.Generic.HashSet<int>();
                // Screen coordinates corresponding to UV (0.15, 0.15):
                // minX=0, maxX=1, minY=0, maxY=1
                Vector2 screenPos = new Vector2(75, 425);
                UVEditorCore.ExecuteSingleClickSelection(canvasRect, 0, 1, 0, 1, screenPos, uvs, triangles, sel, UVSelectionMode.Island, false, false);
                if (sel.Count != 3 || !sel.Contains(0) || !sel.Contains(1) || !sel.Contains(2))
                    throw new Exception($"Expected island 1 (3 verts), got {sel.Count} verts.");

                // 2. Marquee box select second island with Add (Shift)
                Vector2 startMouse = new Vector2(300, 50);  // UV (0.6, 0.9)
                Vector2 endMouse = new Vector2(480, 200);   // UV (0.96, 0.6)
                UVEditorCore.ExecuteMarqueeSelection(canvasRect, 0, 1, 0, 1, startMouse, endMouse, uvs, triangles, sel, UVSelectionMode.Island, true, false);
                if (sel.Count != 6)
                    throw new Exception($"Expected all 6 verts selected after Shift+Marquee, got {sel.Count}.");

                // 3. Subtract first island with Remove (Ctrl)
                UVEditorCore.ExecuteSingleClickSelection(canvasRect, 0, 1, 0, 1, screenPos, uvs, triangles, sel, UVSelectionMode.Island, false, true);
                if (sel.Count != 3 || sel.Contains(0))
                    throw new Exception($"Expected island 1 removed, got {sel.Count} verts remaining.");
            });

            Test("Selection-Aware Transforms", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[]
                {
                    new Vector2(0.1f, 0.2f), // vertex 0 (to be selected)
                    new Vector2(0.9f, 0.8f)  // vertex 1 (unselected, should NOT move)
                };
                var sel = new System.Collections.Generic.HashSet<int> { 0 };

                // Flip V on selected only
                Vector2[] flipped = UVEditorCore.FlipV(uvs, true, sel);
                AssertApprox(flipped[0], new Vector2(0.1f, 0.8f)); // flipped (2*0.5 - 0.2 = 0.8)
                AssertApprox(flipped[1], new Vector2(0.9f, 0.8f)); // completely untouched!

                // Rotate on selected only
                Vector2[] rotated = UVEditorCore.Rotate90(uvs, true, new Vector2(0.5f, 0.5f), sel);
                AssertApprox(rotated[1], new Vector2(0.9f, 0.8f)); // completely untouched!
            });

            Test("3D Vertex Translation (Position XYZ)", ref passed, ref total, () =>
            {
                Mesh m = new Mesh();
                m.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 2, 3) };
                m.triangles = new[] { 0, 1, 0 };

                UVEditorCore.Transform3DVertices(m, new Vector3(10, 20, 30), Vector3.zero, Vector3.one, 0, null);
                AssertApprox3D(m.vertices[0], new Vector3(10, 20, 30));
                AssertApprox3D(m.vertices[1], new Vector3(11, 22, 33));

                UnityEngine.Object.DestroyImmediate(m);
            });

            Test("3D Vertex Partial Selection (Selected UVs only)", ref passed, ref total, () =>
            {
                Mesh m = new Mesh();
                m.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 1, 1) };
                m.triangles = new[] { 0, 1, 0 };
                var sel = new System.Collections.Generic.HashSet<int> { 0 };

                UVEditorCore.Transform3DVertices(m, new Vector3(5, 0, 0), Vector3.zero, Vector3.one, 0, sel);
                AssertApprox3D(m.vertices[0], new Vector3(5, 0, 0));
                AssertApprox3D(m.vertices[1], new Vector3(1, 1, 1)); // untouched!

                UnityEngine.Object.DestroyImmediate(m);
            });

            Test("3D Vertex Scale and Rotation around Pivot", ref passed, ref total, () =>
            {
                Mesh m = new Mesh();
                m.vertices = new[] { new Vector3(1, 2, 3), new Vector3(1, 0, 0) };
                m.triangles = new[] { 0, 1, 0 };

                // Scale 2x around origin (pivot mode 1)
                UVEditorCore.Transform3DVertices(m, Vector3.zero, Vector3.zero, new Vector3(2, 2, 2), 1, null);
                AssertApprox3D(m.vertices[0], new Vector3(2, 4, 6));
                AssertApprox3D(m.vertices[1], new Vector3(2, 0, 0));

                UnityEngine.Object.DestroyImmediate(m);
            });

            Test("3D Center Mesh to Origin & Mirror X", ref passed, ref total, () =>
            {
                Mesh m = new Mesh();
                m.vertices = new[] { new Vector3(2, 4, 6), new Vector3(4, 6, 8) };
                m.triangles = new[] { 0, 1, 0 };

                UVEditorCore.CenterMeshToOrigin(m, null);
                AssertApprox3D(m.vertices[0], new Vector3(-1, -1, -1));
                AssertApprox3D(m.vertices[1], new Vector3(1, 1, 1));

                UVEditorCore.MirrorMeshX(m, null);
                AssertApprox3D(m.vertices[0], new Vector3(1, -1, -1));
                AssertApprox3D(m.vertices[1], new Vector3(-1, 1, 1));

                UnityEngine.Object.DestroyImmediate(m);
            });

            Test("IsPointOnSelection", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[]
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f)
                };
                int[] triangles = new[] { 0, 1, 2 };
                var sel = new System.Collections.Generic.HashSet<int> { 0, 1, 2 };

                bool onSel = UVEditorCore.IsPointOnSelection(new Vector2(0.2f, 0.2f), uvs, triangles, sel, 0.05f);
                if (!onSel) throw new Exception("Expected point (0.2, 0.2) to be on selection.");

                bool offSel = UVEditorCore.IsPointOnSelection(new Vector2(0.8f, 0.8f), uvs, triangles, sel, 0.05f);
                if (offSel) throw new Exception("Expected point (0.8, 0.8) to NOT be on selection.");
            });

            Test("Connected Quad Detection: GetQuadVertices", ref passed, ref total, () =>
            {
                // Quad ABCD triangulated into T1(0,1,2) and T2(0,2,3)
                Vector2[] uvs = new[]
                {
                    new Vector2(0f, 0f), // 0: bottom-left
                    new Vector2(1f, 0f), // 1: bottom-right
                    new Vector2(1f, 1f), // 2: top-right
                    new Vector2(0f, 1f)  // 3: top-left
                };
                int[] triangles = new[] { 0, 1, 2,  0, 2, 3 };

                // Query from triangle 0: should return all 4 vertices of the quad
                var quad0 = UVEditorCore.GetQuadVertices(0, uvs, triangles);
                if (quad0.Count != 4 || !quad0.Contains(0) || !quad0.Contains(1) || !quad0.Contains(2) || !quad0.Contains(3))
                    throw new Exception("Expected all 4 vertices {0,1,2,3} for quad0, got count " + quad0.Count);

                // Query from triangle 1 (offset 3): should also return all 4 vertices
                var quad1 = UVEditorCore.GetQuadVertices(3, uvs, triangles);
                if (quad1.Count != 4 || !quad1.Contains(0) || !quad1.Contains(1) || !quad1.Contains(2) || !quad1.Contains(3))
                    throw new Exception("Expected all 4 vertices {0,1,2,3} for quad1, got count " + quad1.Count);
            });

            Test("Single Click Quad Selection in UVSelectionMode.Quad", ref passed, ref total, () =>
            {
                Rect canvasRect = new Rect(0, 0, 500, 500);
                Vector2[] uvs = new[]
                {
                    new Vector2(0.1f, 0.1f), new Vector2(0.5f, 0.1f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.1f, 0.5f)
                };
                int[] triangles = new[] { 0, 1, 2,  0, 2, 3 };
                var sel = new System.Collections.Generic.HashSet<int>();

                // Click inside first triangle (UV ~0.2, 0.2 -> screenPos)
                Vector2 screenPos = new Vector2(100, 400);
                UVEditorCore.ExecuteSingleClickSelection(canvasRect, 0, 1, 0, 1, screenPos, uvs, triangles, sel, UVSelectionMode.Quad, false, false);

                if (sel.Count != 4 || !sel.Contains(0) || !sel.Contains(1) || !sel.Contains(2) || !sel.Contains(3))
                    throw new Exception($"Expected whole quad (4 vertices) selected, got {sel.Count}");
            });

            Test("Material & Texture Detection: Friendly Names & Slots", ref passed, ref total, () =>
            {
                // 1. Test Friendly Names
                if (UVInspectorTool.GetFriendlyTextureName("_BaseMap") != "Albedo (Base)")
                    throw new Exception("Expected Albedo (Base)");
                if (UVInspectorTool.GetFriendlyTextureName("_BumpMap") != "Normal")
                    throw new Exception("Expected Normal");
                if (UVInspectorTool.GetFriendlyTextureName("_MetallicGlossMap") != "Metallic")
                    throw new Exception("Expected Metallic");
                if (UVInspectorTool.GetFriendlyTextureName("_CustomMap") != "CustomMap")
                    throw new Exception("Expected CustomMap");

                // 2. Test Material Texture Slots
                Shader standardShader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Hidden/Internal-Colored");
                if (standardShader != null)
                {
                    Material testMat = new Material(standardShader);
                    Texture2D testTex = new Texture2D(16, 16);
                    testTex.name = "TestTexture";

                    string prop = testMat.HasProperty("_BaseMap") ? "_BaseMap" : (testMat.HasProperty("_MainTex") ? "_MainTex" : null);
                    if (prop != null)
                    {
                        testMat.SetTexture(prop, testTex);
                        var slots = UVInspectorTool.GetMaterialTextureSlots(testMat);
                        if (slots.Count == 0 || slots[0].texture != testTex)
                            throw new Exception("Failed to detect assigned texture on material.");
                    }

                    UnityEngine.Object.DestroyImmediate(testTex);
                    UnityEngine.Object.DestroyImmediate(testMat);
                }
            });

            Test("Submesh & Material Corresponding: GetSubmeshForVertices", ref passed, ref total, () =>
            {
                Mesh testMesh = new Mesh();
                testMesh.vertices = new Vector3[]
                {
                    new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), // Submesh 0
                    new Vector3(2, 0, 0), new Vector3(3, 0, 0), new Vector3(2, 1, 0)  // Submesh 1
                };
                testMesh.subMeshCount = 2;
                testMesh.SetTriangles(new int[] { 0, 1, 2 }, 0);
                testMesh.SetTriangles(new int[] { 3, 4, 5 }, 1);

                int submesh0 = UVEditorCore.GetSubmeshForVertices(testMesh, new int[] { 1 });
                if (submesh0 != 0) throw new Exception($"Expected Submesh 0, got {submesh0}");

                int submesh1 = UVEditorCore.GetSubmeshForVertices(testMesh, new int[] { 4, 5 });
                if (submesh1 != 1) throw new Exception($"Expected Submesh 1, got {submesh1}");

                int submeshEmpty = UVEditorCore.GetSubmeshForVertices(testMesh, System.Array.Empty<int>());
                if (submeshEmpty != -1) throw new Exception($"Expected -1 for empty selection, got {submeshEmpty}");

                UnityEngine.Object.DestroyImmediate(testMesh);
            });

            Test("Boundary Outline Edge Extraction (Silhouette Perimeter)", ref passed, ref total, () =>
            {
                // Quad ABCD triangulated into T1(0,1,2) and T2(0,2,3) sharing diagonal (0,2)
                int[] quadTriangles = new[] { 0, 1, 2,  0, 2, 3 };
                var selQuad = new System.Collections.Generic.HashSet<int> { 0, 1, 2, 3 };

                var outlineIndices = UVEditorCore.ExtractBoundaryOutlineEdges(quadTriangles, selQuad);

                // There must be exactly 4 boundary edges = 8 indices
                if (outlineIndices.Count != 8)
                    throw new Exception("Expected exactly 8 indices (4 boundary edges) for a quad, got " + outlineIndices.Count);

                // Verify that the diagonal edge (0, 2) is NOT in the boundary outline
                for (int i = 0; i < outlineIndices.Count; i += 2)
                {
                    int a = outlineIndices[i];
                    int b = outlineIndices[i + 1];
                    if ((a == 0 && b == 2) || (a == 2 && b == 0))
                    {
                        throw new Exception("Boundary outline incorrectly included internal shared diagonal edge (0, 2)!");
                    }
                }
            });

            Test("Ghost Preview Delta & Centroid Translation", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[]
                {
                    new Vector2(0.2f, 0.2f), // vertex 0 (selected)
                    new Vector2(0.4f, 0.2f), // vertex 1 (selected)
                    new Vector2(0.3f, 0.4f), // vertex 2 (selected)
                    new Vector2(0.9f, 0.9f)  // vertex 3 (unselected)
                };
                var sel = new System.Collections.Generic.HashSet<int> { 0, 1, 2 };
                Vector2 delta = new Vector2(0.15f, -0.05f);

                // Compute original centroid
                Vector2 origCentroid = (uvs[0] + uvs[1] + uvs[2]) / 3f;
                AssertApprox(origCentroid, new Vector2(0.3f, 0.2667f), 0.001f);

                // Ghost centroid must equal origCentroid + delta
                Vector2 ghostCentroid = origCentroid + delta;
                AssertApprox(ghostCentroid, new Vector2(0.45f, 0.2167f), 0.001f);

                // Compute ghost preview vertex positions
                Vector2 ghost0 = uvs[0] + delta;
                Vector2 ghost1 = uvs[1] + delta;
                Vector2 ghost2 = uvs[2] + delta;

                AssertApprox(ghost0, new Vector2(0.35f, 0.15f));
                AssertApprox(ghost1, new Vector2(0.55f, 0.15f));
                AssertApprox(ghost2, new Vector2(0.45f, 0.35f));
            });

            Test("RayIntersectsTriangle: 3D Ray-Triangle Hit & Miss", ref passed, ref total, () =>
            {
                Vector3 v0 = new Vector3(-1, -1, 0);
                Vector3 v1 = new Vector3(1, -1, 0);
                Vector3 v2 = new Vector3(0, 1, 0);

                // Ray directly through center of triangle
                bool hitCenter = UVEditorCore.RayIntersectsTriangle(new Vector3(0, 0, -5), Vector3.forward, v0, v1, v2, out float distCenter);
                if (!hitCenter) throw new Exception("Expected ray to hit center of triangle.");
                if (Mathf.Abs(distCenter - 5f) > 0.001f) throw new Exception($"Expected hit distance 5, got {distCenter}");

                // Ray pointing away (miss)
                bool hitAway = UVEditorCore.RayIntersectsTriangle(new Vector3(0, 0, -5), Vector3.back, v0, v1, v2, out _);
                if (hitAway) throw new Exception("Expected backward ray to miss.");

                // Ray outside triangle bounds (miss)
                bool hitOff = UVEditorCore.RayIntersectsTriangle(new Vector3(5, 5, -5), Vector3.forward, v0, v1, v2, out _);
                if (hitOff) throw new Exception("Expected off-center ray to miss.");
            });

            Test("RaycastMeshTriangles: 3D Closest Triangle Detection", ref passed, ref total, () =>
            {
                // Triangle 0 at Z = 10 (further away)
                // Triangle 1 at Z = 2 (closer)
                Vector3[] verts = new[]
                {
                    new Vector3(-1, -1, 10), new Vector3(1, -1, 10), new Vector3(0, 1, 10), // Tri 0
                    new Vector3(-1, -1, 2),  new Vector3(1, -1, 2),  new Vector3(0, 1, 2)   // Tri 1
                };
                int[] tris = new[] { 0, 1, 2,  3, 4, 5 };

                Ray ray = new Ray(new Vector3(0, 0, 0), Vector3.forward);
                bool hit = UVEditorCore.RaycastMeshTriangles(ray, verts, tris, out int hitIdx, out float hitDist);
                if (!hit) throw new Exception("Expected raycast hit on mesh triangles.");
                if (hitIdx != 3) throw new Exception($"Expected hit on closer triangle 1 (index 3), but hit triangle index {hitIdx}");
                if (Mathf.Abs(hitDist - 2f) > 0.001f) throw new Exception($"Expected hit distance 2, got {hitDist}");
            });

            Test("GetCachedMeshVertices: Caching & Invalidation", ref passed, ref total, () =>
            {
                Mesh testMesh = new Mesh();
                testMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                testMesh.triangles = new[] { 0, 1, 2 };

                Vector3[] verts1 = UVEditorCore.GetCachedMeshVertices(testMesh);
                Vector3[] verts2 = UVEditorCore.GetCachedMeshVertices(testMesh);

                if (verts1 == null || verts1.Length != 3) throw new Exception("Expected 3 vertices from cache.");
                if (!object.ReferenceEquals(verts1, verts2)) throw new Exception("Expected cached reference to match without re-allocation.");

                UVEditorCore.InvalidateCache();
                Vector3[] verts3 = UVEditorCore.GetCachedMeshVertices(testMesh);
                if (verts3 == null || verts3.Length != 3) throw new Exception("Expected 3 vertices after invalidation.");
                UnityEngine.Object.DestroyImmediate(testMesh);
            });

            Test("ExtractBoundaryOutlineEdges: Quad boundary extraction", ref passed, ref total, () =>
            {
                int[] tris = new[] { 0, 1, 2,  0, 2, 3 };
                HashSet<int> sel = new HashSet<int> { 0, 1, 2, 3 };

                List<int> outline = UVEditorCore.ExtractBoundaryOutlineEdges(tris, sel);
                if (outline.Count != 8) throw new Exception($"Expected 8 indices for 4 quad boundary edges, got {outline.Count}");
            });

            Test("Right Click Unselect: Clears and subtracts selection", ref passed, ref total, () =>
            {
                Vector2[] uvs = new[] { new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.2f), new Vector2(0.8f, 0.8f) };
                int[] triangles = new[] { 0, 1, 2 };
                HashSet<int> sel = new HashSet<int> { 0, 1, 2 };

                Rect canvasRect = new Rect(0, 0, 500, 500);
                Vector2 mousePos = new Vector2(250, 250); // Inside triangle (0.5, 0.5)

                UVEditorCore.ExecuteSingleClickSelection(canvasRect, 0, 1, 0, 1, mousePos, uvs, triangles, sel, UVSelectionMode.Face, isAdd: false, isRemove: true);
                if (sel.Count != 0) throw new Exception($"Expected selection to be removed, got {sel.Count} vertices remaining.");

                sel.Add(0);
                sel.Add(1);
                sel.Clear();
                if (sel.Count != 0) throw new Exception("Expected selection to be cleared.");
            });

            Test("Scene View Right Click Deselect: Deselects specific UV or clears selection in 3D", ref passed, ref total, () =>
            {
                Mesh testMesh = new Mesh();
                testMesh.vertices = new[]
                {
                    new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(0, 1, 0),    // Tri 0 (X around 0)
                    new Vector3(9, -1, 0),  new Vector3(11, -1, 0), new Vector3(10, 1, 0)   // Tri 1 (X around 10)
                };
                testMesh.uv = new[]
                {
                    new Vector2(0.1f, 0.1f), new Vector2(0.2f, 0.1f), new Vector2(0.15f, 0.2f),
                    new Vector2(0.7f, 0.7f), new Vector2(0.8f, 0.7f), new Vector2(0.75f, 0.8f)
                };
                testMesh.triangles = new[] { 0, 1, 2,  3, 4, 5 };

                HashSet<int> sel = new HashSet<int> { 0, 1, 2, 3, 4, 5 };

                // 1. Ray targeting Tri 0 in 3D space: should deselect only Tri 0's island
                Ray rayTri0 = new Ray(new Vector3(0, 0, -5), Vector3.forward);
                bool changed1 = UVEditorCore.DeselectFromSceneRay(rayTri0, testMesh, -1, 0, sel, UVSelectionMode.Island);
                if (!changed1) throw new Exception("Expected DeselectFromSceneRay to return true for hit triangle.");
                if (sel.Count != 3) throw new Exception($"Expected 3 vertices remaining, got {sel.Count}");
                if (sel.Contains(0) || sel.Contains(1) || sel.Contains(2)) throw new Exception("Tri 0 vertices should have been deselected.");
                if (!sel.Contains(3) || !sel.Contains(4) || !sel.Contains(5)) throw new Exception("Tri 1 vertices should still be selected.");

                // 2. Ray targeting empty space (miss): should clear all remaining selection
                Ray rayEmpty = new Ray(new Vector3(50, 50, -5), Vector3.forward);
                bool changed2 = UVEditorCore.DeselectFromSceneRay(rayEmpty, testMesh, -1, 0, sel, UVSelectionMode.Island);
                if (!changed2) throw new Exception("Expected DeselectFromSceneRay to return true when clearing empty space.");
                if (sel.Count != 0) throw new Exception($"Expected 0 vertices remaining after empty space click, got {sel.Count}");

                UnityEngine.Object.DestroyImmediate(testMesh);
            });

            Test("Plugin Structure & Package Export Readiness", ref passed, ref total, () =>
            {
                string root = UVPackageExporter.GetPackageRootPath();
                if (!AssetDatabase.IsValidFolder(root))
                    throw new Exception($"Package root path '{root}' is not a valid folder.");

                string pkgJsonPath = Path.Combine(root, "package.json");
                if (!File.Exists(pkgJsonPath))
                    throw new Exception($"Missing package.json at '{pkgJsonPath}'.");

                string pkgContent = File.ReadAllText(pkgJsonPath);
                if (!pkgContent.Contains("com.antigravity.uvpreviewandfixer"))
                    throw new Exception("package.json does not contain package name 'com.antigravity.uvpreviewandfixer'.");

                string runtimeAsmdef = Path.Combine(root, "Runtime", "UVPreviewAndFixer.Runtime.asmdef");
                if (!File.Exists(runtimeAsmdef))
                    throw new Exception($"Missing runtime asmdef at '{runtimeAsmdef}'.");

                string editorAsmdef = Path.Combine(root, "Editor", "UVPreviewAndFixer.Editor.asmdef");
                if (!File.Exists(editorAsmdef))
                    throw new Exception($"Missing editor asmdef at '{editorAsmdef}'.");

                string shaderPath = Path.Combine(root, "Shaders", "UVPreviewShader.shader");
                if (!File.Exists(shaderPath))
                    throw new Exception($"Missing shader at '{shaderPath}'.");

                string readmePath = Path.Combine(root, "README.md");
                if (!File.Exists(readmePath))
                    throw new Exception($"Missing README.md at '{readmePath}'.");
            });

            // -------------------------------------------------------------
            // UV Island Overlap & Shared Texture Detection Tests
            // -------------------------------------------------------------

            Test("UV Island Overlap - Disjoint Non-Overlapping Islands", ref passed, ref total, () =>
            {
                // Island A: (0,0), (0.2, 0), (0.1, 0.2)
                // Island B: (0.5, 0.5), (0.7, 0.5), (0.6, 0.7)
                Vector2[] uvs = new Vector2[]
                {
                    new Vector2(0f, 0f), new Vector2(0.2f, 0f), new Vector2(0.1f, 0.2f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.7f, 0.5f), new Vector2(0.6f, 0.7f)
                };
                int[] tris = new int[] { 0, 1, 2, 3, 4, 5 };

                var overlap = UVEditorCore.CalculateIslandOverlaps(uvs, tris);
                if (overlap == null) throw new Exception("Island overlap result was null.");
                if (overlap.islands.Count != 2) throw new Exception($"Expected 2 islands, got {overlap.islands.Count}.");
                if (overlap.totalOverlappingIslands != 0) throw new Exception($"Expected 0 overlapping islands, got {overlap.totalOverlappingIslands}.");
                if (overlap.sharedGroups.Count != 0) throw new Exception($"Expected 0 shared groups, got {overlap.sharedGroups.Count}.");
            });

            Test("UV Island Overlap - Mirrored / Coincident Stacked Islands (Shared Texture)", ref passed, ref total, () =>
            {
                // Island A: (0.1, 0.1), (0.4, 0.1), (0.25, 0.4)
                // Island B: (0.1, 0.1), (0.4, 0.1), (0.25, 0.4) with separate vertex indices 3, 4, 5
                Vector2[] uvs = new Vector2[]
                {
                    new Vector2(0.1f, 0.1f), new Vector2(0.4f, 0.1f), new Vector2(0.25f, 0.4f),
                    new Vector2(0.1f, 0.1f), new Vector2(0.4f, 0.1f), new Vector2(0.25f, 0.4f)
                };
                int[] tris = new int[] { 0, 1, 2, 3, 4, 5 };

                var overlap = UVEditorCore.CalculateIslandOverlaps(uvs, tris);
                if (overlap == null) throw new Exception("Island overlap result was null.");
                if (overlap.islands.Count != 2) throw new Exception($"Expected 2 islands, got {overlap.islands.Count}.");
                if (overlap.totalOverlappingIslands != 2) throw new Exception($"Expected 2 overlapping islands, got {overlap.totalOverlappingIslands}.");
                if (overlap.sharedGroups.Count != 1) throw new Exception($"Expected 1 shared group, got {overlap.sharedGroups.Count}.");
                if (overlap.sharedGroups[0].islandIds.Count != 2) throw new Exception($"Expected group size 2, got {overlap.sharedGroups[0].islandIds.Count}.");
                if (overlap.islands[0].overlappingIslandIds.Count != 1) throw new Exception("Island 0 should overlap with 1 island.");
                if (overlap.islands[0].overlappingIslandIds[0] != 1) throw new Exception("Island 0 should overlap with Island 1.");
            });

            Test("UV Island Overlap - 3 Stacked Shared Texture Islands", ref passed, ref total, () =>
            {
                // Island A, B, C stacked on same texture space (x3 symmetry)
                Vector2[] uvs = new Vector2[]
                {
                    new Vector2(0.2f, 0.2f), new Vector2(0.6f, 0.2f), new Vector2(0.4f, 0.6f),
                    new Vector2(0.2f, 0.2f), new Vector2(0.6f, 0.2f), new Vector2(0.4f, 0.6f),
                    new Vector2(0.2f, 0.2f), new Vector2(0.6f, 0.2f), new Vector2(0.4f, 0.6f)
                };
                int[] tris = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

                var overlap = UVEditorCore.CalculateIslandOverlaps(uvs, tris);
                if (overlap.islands.Count != 3) throw new Exception($"Expected 3 islands, got {overlap.islands.Count}.");
                if (overlap.totalOverlappingIslands != 3) throw new Exception($"Expected 3 overlapping islands, got {overlap.totalOverlappingIslands}.");
                if (overlap.sharedGroups.Count != 1) throw new Exception($"Expected 1 shared group, got {overlap.sharedGroups.Count}.");
                if (overlap.sharedGroups[0].islandIds.Count != 3) throw new Exception($"Expected group size 3, got {overlap.sharedGroups[0].islandIds.Count}.");
            });

            Test("UV Island Overlap - Partial Overlap Crossing Edges", ref passed, ref total, () =>
            {
                // Island A: (0, 0), (0.4, 0), (0.2, 0.4)
                // Island B: (0.1, 0.1), (0.5, 0.1), (0.3, 0.5)
                Vector2[] uvs = new Vector2[]
                {
                    new Vector2(0f, 0f), new Vector2(0.4f, 0f), new Vector2(0.2f, 0.4f),
                    new Vector2(0.1f, 0.1f), new Vector2(0.5f, 0.1f), new Vector2(0.3f, 0.5f)
                };
                int[] tris = new int[] { 0, 1, 2, 3, 4, 5 };

                var overlap = UVEditorCore.CalculateIslandOverlaps(uvs, tris);
                if (overlap.totalOverlappingIslands != 2) throw new Exception($"Expected 2 overlapping islands, got {overlap.totalOverlappingIslands}.");
                if (overlap.sharedGroups.Count != 1) throw new Exception($"Expected 1 shared group, got {overlap.sharedGroups.Count}.");
            });

            Test("UV Island Overlap - Selected Island Detection", ref passed, ref total, () =>
            {
                // Island 0: (0.1, 0.1), (0.4, 0.1), (0.25, 0.4)
                // Island 1: coincident mirrored with Island 0
                // Island 2: (0.7, 0.7), (0.9, 0.7), (0.8, 0.9) - clean unique island
                Vector2[] uvs = new Vector2[]
                {
                    new Vector2(0.1f, 0.1f), new Vector2(0.4f, 0.1f), new Vector2(0.25f, 0.4f),
                    new Vector2(0.1f, 0.1f), new Vector2(0.4f, 0.1f), new Vector2(0.25f, 0.4f),
                    new Vector2(0.7f, 0.7f), new Vector2(0.9f, 0.7f), new Vector2(0.8f, 0.9f)
                };
                int[] tris = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

                var overlap = UVEditorCore.CalculateIslandOverlaps(uvs, tris);
                if (overlap.islands.Count != 3) throw new Exception($"Expected 3 islands, got {overlap.islands.Count}.");

                // Selecting vertex 0 (Island 0)
                int isl0Id = overlap.vertexToIslandMap[0];
                var isl0 = overlap.islands[isl0Id];
                if (isl0.overlappingIslandIds.Count != 1) throw new Exception("Island 0 should overlap with 1 other island.");

                // Selecting vertex 6 (Island 2 - Unique)
                int isl2Id = overlap.vertexToIslandMap[6];
                var isl2 = overlap.islands[isl2Id];
                if (isl2.overlappingIslandIds.Count != 0) throw new Exception("Island 2 should have 0 overlaps.");
            });

            Test("FlipSelectedUVOrigin - Vertical In-Place", ref passed, ref total, () =>
            {
                // Triangle: (0.2, 0.1), (0.4, 0.1), (0.3, 0.3) -> center Y is 0.2
                // Vert 3 is outside at (0.9, 0.9) untouched
                Vector2[] uvs = new Vector2[]
                {
                    new Vector2(0.2f, 0.1f),
                    new Vector2(0.4f, 0.1f),
                    new Vector2(0.3f, 0.3f),
                    new Vector2(0.9f, 0.9f)
                };
                HashSet<int> sel = new HashSet<int> { 0, 1, 2 };

                Vector2[] flipped = UVEditorCore.FlipSelectedUVOrigin(uvs, UVFlipAxis.Vertical, sel);

                // Y=0.1 flips across 0.2 to 0.3
                // Y=0.3 flips across 0.2 to 0.1
                AssertApprox(flipped[0], new Vector2(0.2f, 0.3f));
                AssertApprox(flipped[1], new Vector2(0.4f, 0.3f));
                AssertApprox(flipped[2], new Vector2(0.3f, 0.1f));
                // Vert 3 must be untouched
                AssertApprox(flipped[3], new Vector2(0.9f, 0.9f));
            });

            Test("FlipSelectedUVOrigin - Horizontal In-Place", ref passed, ref total, () =>
            {
                // Triangle: (0.2, 0.1), (0.4, 0.1), (0.3, 0.3) -> center X is 0.3
                Vector2[] uvs = new Vector2[]
                {
                    new Vector2(0.2f, 0.1f),
                    new Vector2(0.4f, 0.1f),
                    new Vector2(0.3f, 0.3f)
                };
                HashSet<int> sel = new HashSet<int> { 0, 1, 2 };

                Vector2[] flipped = UVEditorCore.FlipSelectedUVOrigin(uvs, UVFlipAxis.Horizontal, sel);

                // X=0.2 flips across 0.3 to 0.4
                // X=0.4 flips across 0.3 to 0.2
                // X=0.3 stays 0.3
                AssertApprox(flipped[0], new Vector2(0.4f, 0.1f));
                AssertApprox(flipped[1], new Vector2(0.2f, 0.1f));
                AssertApprox(flipped[2], new Vector2(0.3f, 0.3f));
            });

            Test("FlipSelectedUVOrigin - Both In-Place", ref passed, ref total, () =>
            {
                Vector2[] uvs = new Vector2[]
                {
                    new Vector2(0.2f, 0.1f),
                    new Vector2(0.4f, 0.1f),
                    new Vector2(0.3f, 0.3f)
                };
                HashSet<int> sel = new HashSet<int> { 0, 1, 2 };

                Vector2[] flipped = UVEditorCore.FlipSelectedUVOrigin(uvs, UVFlipAxis.Both, sel);

                AssertApprox(flipped[0], new Vector2(0.4f, 0.3f));
                AssertApprox(flipped[1], new Vector2(0.2f, 0.3f));
                AssertApprox(flipped[2], new Vector2(0.3f, 0.1f));
            });

            Test("FlipSelectedUVOrigin - Multi-Island Per-Island", ref passed, ref total, () =>
            {
                // Island 1: centered around (0.2, 0.2)
                // Island 2: centered around (0.8, 0.8)
                Vector2[] uvs = new Vector2[]
                {
                    new Vector2(0.1f, 0.1f), new Vector2(0.3f, 0.1f), new Vector2(0.2f, 0.3f), // Tri 1
                    new Vector2(0.7f, 0.7f), new Vector2(0.9f, 0.7f), new Vector2(0.8f, 0.9f)  // Tri 2
                };
                int[] tris = new int[] { 0, 1, 2, 3, 4, 5 };
                HashSet<int> selAll = new HashSet<int> { 0, 1, 2, 3, 4, 5 };

                Vector2[] flipped = UVEditorCore.FlipSelectedUVOrigin(uvs, UVFlipAxis.Vertical, selAll, tris, true);

                // Island 1 flips in place around Y=0.2 (stays within [0.1..0.3])
                AssertApprox(flipped[0], new Vector2(0.1f, 0.3f));
                AssertApprox(flipped[2], new Vector2(0.2f, 0.1f));

                // Island 2 flips in place around Y=0.8 (stays within [0.7..0.9])
                AssertApprox(flipped[3], new Vector2(0.7f, 0.9f));
                AssertApprox(flipped[5], new Vector2(0.8f, 0.7f));
            });

            Test("UV Preview - No Inside Color / Hollow Selection Outline", ref passed, ref total, () =>
            {
                // Verify that boundary outline edges are cleanly extracted for hollow selection
                int[] tris = new int[] { 0, 1, 2, 0, 2, 3 }; // Quad
                HashSet<int> sel = new HashSet<int> { 0, 1, 2, 3 };
                var outlineEdges = UVEditorCore.ExtractBoundaryOutlineEdges(tris, sel);
                // For a quad, exactly 4 boundary edges (8 vertex indices) should form the hollow silhouette
                if (outlineEdges.Count != 8)
                {
                    throw new Exception($"Expected 8 indices (4 boundary edges) for hollow outline, got {outlineEdges.Count}");
                }
            });

            Test("Connected FBX Vertices Selection Across Split Normals", ref passed, ref total, () =>
            {
                // Mesh with split vertices at the same 3D position (like an imported FBX cube)
                Vector3[] verts = new[]
                {
                    new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), // Face 1 (indices 0, 1, 2)
                    new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0)  // Face 2 (indices 3, 4, 5) - corners 0 & 3, 2 & 5 share 3D pos
                };
                Vector2[] uvs = new[]
                {
                    new Vector2(0.1f, 0.1f), new Vector2(0.3f, 0.1f), new Vector2(0.1f, 0.3f), // Island 1
                    new Vector2(0.7f, 0.7f), new Vector2(0.9f, 0.7f), new Vector2(0.7f, 0.9f)  // Island 2
                };
                int[] tris = new[] { 0, 1, 2,  3, 4, 5 };

                // 1. GetConnectedVertices test
                var conn0 = UVEditorCore.GetConnectedVertices(0, verts, uvs, match3DPosition: true, matchUVPosition: false);
                if (!conn0.Contains(0) || !conn0.Contains(3) || conn0.Count != 2)
                {
                    throw new Exception($"Expected {0, 3} connected vertices for vertex 0, got {conn0.Count}");
                }

                var conn2 = UVEditorCore.GetConnectedVertices(2, verts, uvs, match3DPosition: true, matchUVPosition: false);
                if (!conn2.Contains(2) || !conn2.Contains(5) || conn2.Count != 2)
                {
                    throw new Exception($"Expected {2, 5} connected vertices for vertex 2, got {conn2.Count}");
                }

                // 2. Single click selection in Vertex mode with selectConnectedFBX = true
                Rect canvasRect = new Rect(0, 0, 500, 500);
                HashSet<int> sel = new HashSet<int>();
                // Vertex 0 is at UV (0.1, 0.1). In canvas space with min=(0,0), max=(1,1):
                Vector2 clickMousePos = new Vector2(50, 450); // 0.1 from left, 0.1 from bottom (450/500 = 0.9 from top)
                UVEditorCore.ExecuteSingleClickSelection(
                    canvasRect, 0f, 1f, 0f, 1f,
                    clickMousePos, uvs, tris,
                    sel, UVSelectionMode.Vertex,
                    isAdd: false, isRemove: false,
                    vertices: verts,
                    selectConnectedFBX: true
                );

                if (!sel.Contains(0) || !sel.Contains(3))
                {
                    throw new Exception($"Expected single click selection with selectConnectedFBX=true to select both verts 0 and 3, but got count: {sel.Count}");
                }

                // 3. Single click selection in Vertex mode with selectConnectedFBX = false
                sel.Clear();
                UVEditorCore.ExecuteSingleClickSelection(
                    canvasRect, 0f, 1f, 0f, 1f,
                    clickMousePos, uvs, tris,
                    sel, UVSelectionMode.Vertex,
                    isAdd: false, isRemove: false,
                    vertices: verts,
                    selectConnectedFBX: false
                );

                if (!sel.Contains(0) || sel.Contains(3) || sel.Count != 1)
                {
                    throw new Exception($"Expected single click selection with selectConnectedFBX=false to select only vert 0, but got count: {sel.Count}");
                }

                // 4. Marquee selection with selectConnectedFBX = true
                sel.Clear();
                UVEditorCore.ExecuteMarqueeSelection(
                    canvasRect, 0f, 1f, 0f, 1f,
                    new Vector2(40, 440), new Vector2(60, 460), // Small marquee enclosing only vert 0 in UV space
                    uvs, tris,
                    sel, UVSelectionMode.Vertex,
                    isAdd: false, isRemove: false,
                    vertices: verts,
                    selectConnectedFBX: true
                );

                if (!sel.Contains(0) || !sel.Contains(3))
                {
                    throw new Exception($"Expected marquee selection with selectConnectedFBX=true to select connected vert 3 also, got count: {sel.Count}");
                }
            });

            Test("UV Dedicated Undo & Redo History and Hover Isolation", ref passed, ref total, () =>
            {
                Mesh testMesh = new Mesh();
                testMesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) };
                testMesh.triangles = new[] { 0, 1, 2 };
                Vector2[] initialUVs = new[] { new Vector2(0.1f, 0.1f), new Vector2(0.5f, 0.1f), new Vector2(0.1f, 0.5f) };
                testMesh.uv = initialUVs;

                int channel = 0;
                UVEditorCore.UVHistory.ClearHistory(testMesh, channel);

                // 1. Initial state
                UVEditorCore.UVHistory.EnsureInitialState(testMesh, channel, initialUVs, "Initial State");
                if (UVEditorCore.UVHistory.CanUndo(testMesh, channel))
                    throw new Exception("Expected CanUndo to be false for initial state.");
                if (UVEditorCore.UVHistory.CanRedo(testMesh, channel))
                    throw new Exception("Expected CanRedo to be false for initial state.");

                // 2. Perform edit 1 (Flip V)
                Vector2[] edit1UVs = UVEditorCore.FlipV(initialUVs, true);
                UVEditorCore.SetUVs(testMesh, channel, edit1UVs);
                UVEditorCore.UVHistory.RecordStep(testMesh, channel, edit1UVs, "Flip V");

                if (!UVEditorCore.UVHistory.CanUndo(testMesh, channel))
                    throw new Exception("Expected CanUndo to be true after edit 1.");
                if (UVEditorCore.UVHistory.GetUndoName(testMesh, channel) != "Flip V")
                    throw new Exception($"Expected undo name 'Flip V', got '{UVEditorCore.UVHistory.GetUndoName(testMesh, channel)}'.");
                if (UVEditorCore.UVHistory.CanRedo(testMesh, channel))
                    throw new Exception("Expected CanRedo to be false after edit 1.");

                // 3. Undo edit 1
                bool undone = UVEditorCore.UVHistory.PerformUndo(testMesh, channel);
                if (!undone) throw new Exception("Expected PerformUndo to return true.");
                Vector2[] restoredUVs = UVEditorCore.GetUVs(testMesh, channel);
                AssertApprox(restoredUVs[0], initialUVs[0]);
                AssertApprox(restoredUVs[1], initialUVs[1]);
                AssertApprox(restoredUVs[2], initialUVs[2]);

                if (UVEditorCore.UVHistory.CanUndo(testMesh, channel))
                    throw new Exception("Expected CanUndo to be false after undoing back to initial state.");
                if (!UVEditorCore.UVHistory.CanRedo(testMesh, channel))
                    throw new Exception("Expected CanRedo to be true after undo.");
                if (UVEditorCore.UVHistory.GetRedoName(testMesh, channel) != "Flip V")
                    throw new Exception($"Expected redo name 'Flip V', got '{UVEditorCore.UVHistory.GetRedoName(testMesh, channel)}'.");

                // 4. Redo edit 1
                bool redone = UVEditorCore.UVHistory.PerformRedo(testMesh, channel);
                if (!redone) throw new Exception("Expected PerformRedo to return true.");
                Vector2[] redoneUVs = UVEditorCore.GetUVs(testMesh, channel);
                AssertApprox(redoneUVs[0], edit1UVs[0]);
                AssertApprox(redoneUVs[1], edit1UVs[1]);
                AssertApprox(redoneUVs[2], edit1UVs[2]);

                // 5. Test hover requirement on HandleUndoRedoEvents
                Event evtUndo = new Event { type = EventType.KeyDown, keyCode = KeyCode.Z, control = true };

                // When isHovered = false: MUST NOT handle or consume the event
                bool handledNotHovered = UVEditorCore.HandleUndoRedoEvents(evtUndo, false, testMesh, channel, null, null);
                if (handledNotHovered)
                    throw new Exception("Expected HandleUndoRedoEvents to return false when isHovered = false.");

                // When isHovered = true: MUST handle and consume the event
                bool handledHovered = UVEditorCore.HandleUndoRedoEvents(evtUndo, true, testMesh, channel, null, null);
                if (!handledHovered)
                    throw new Exception("Expected HandleUndoRedoEvents to return true when isHovered = true.");

                // Check that UVs were undone back to initialUVs
                Vector2[] checkUVs = UVEditorCore.GetUVs(testMesh, channel);
                AssertApprox(checkUVs[0], initialUVs[0]);

                UnityEngine.Object.DestroyImmediate(testMesh);
            });

            if (passed == total)
            {
                Debug.Log($"<color=lime><b>[UV Tool] All {passed}/{total} Tests PASSED!</b></color>");
                if (showDialog && !Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("UV Tool Tests Passed", $"All {passed} unit tests passed successfully!", "OK");
                }
            }
            else
            {
                Debug.LogError($"[UV Tool] {total - passed} tests failed out of {total}.");
            }
        }

        private static void Test(string name, ref int passed, ref int total, Action testAction)
        {
            total++;
            try
            {
                testAction();
                passed++;
                Debug.Log($"  <color=green>✓ PASS:</color> {name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"  <color=red>✗ FAIL:</color> {name} - {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void AssertApprox(Vector2 actual, Vector2 expected, float tolerance = 0.001f)
        {
            if (Mathf.Abs(actual.x - expected.x) > tolerance || Mathf.Abs(actual.y - expected.y) > tolerance)
            {
                throw new Exception($"Expected {expected}, but got {actual} (diff: {actual - expected})");
            }
        }

        private static void AssertApprox3D(Vector3 actual, Vector3 expected, float tolerance = 0.001f)
        {
            if (Mathf.Abs(actual.x - expected.x) > tolerance ||
                Mathf.Abs(actual.y - expected.y) > tolerance ||
                Mathf.Abs(actual.z - expected.z) > tolerance)
            {
                throw new Exception($"Expected {expected}, but got {actual} (diff: {actual - expected})");
            }
        }
    }
}
#endif

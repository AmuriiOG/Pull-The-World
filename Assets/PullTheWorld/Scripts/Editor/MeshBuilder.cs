using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// Small procedural mesh kit used to bake every prop in the game.
    ///
    /// Two decisions drive the whole look:
    ///  * Everything is FLAT SHADED (one normal per triangle). That is what gives low-poly art its
    ///    crisp faceting, and it means lighting alone creates the shading gradients - no textures.
    ///  * Boxes are CHAMFERED rather than square. A ~6% bevel catches the key light along every
    ///    edge, which is exactly what makes the reference blocks read as soft and toy-like instead
    ///    of like raw primitives.
    /// </summary>
    public class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly Dictionary<int, List<int>> tris = new Dictionary<int, List<int>>();

        public int VertexCount => verts.Count;

        List<int> Tri(int sub)
        {
            if (!tris.TryGetValue(sub, out var list)) { list = new List<int>(); tris[sub] = list; }
            return list;
        }

        public int AddVertex(Vector3 p, Vector3 n, Vector2 uv)
        {
            verts.Add(p); normals.Add(SafeUnit(n, Vector3.up)); uvs.Add(uv);
            return verts.Count - 1;
        }

        /// <summary>
        /// Unit vector without Vector3.Normalize's 1e-5 cut-off. That cut-off silently zeroed the
        /// face normals of the grass tufts' 0.003-unit tip caps, and a zero normal reaches the
        /// shader as normalize(0) = NaN, which bloom then inflates into a screen-sized white disc.
        /// A mesh must never carry a zero normal, so anything too small to measure gets the hint.
        /// </summary>
        static Vector3 SafeUnit(Vector3 v, Vector3 fallback)
        {
            float sq = v.sqrMagnitude;
            if (sq < 1e-24f || float.IsNaN(sq) || float.IsInfinity(sq))
                return fallback.sqrMagnitude > 1e-24f ? fallback / Mathf.Sqrt(fallback.sqrMagnitude) : Vector3.up;
            return v / Mathf.Sqrt(sq);
        }

        /// <summary>A triangle over vertices already added with AddVertex, in the given winding.</summary>
        public void AddTriangle(int sub, int a, int b, int c)
        {
            var t = Tri(sub); t.Add(a); t.Add(b); t.Add(c);
        }

        // ------------------------------------------------------------------- flat helpers ---
        public void AddFlatTri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 outwardHint)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-16f) return;          // truly degenerate: no area at all
            n = SafeUnit(n, outwardHint);
            if (Vector3.Dot(n, outwardHint) < 0f) { (b, c) = (c, b); n = -n; }

            int i0 = AddVertex(a, n, new Vector2(0f, 0f));
            int i1 = AddVertex(b, n, new Vector2(1f, 0f));
            int i2 = AddVertex(c, n, new Vector2(0.5f, 1f));
            var t = Tri(sub); t.Add(i0); t.Add(i1); t.Add(i2);
        }

        public void AddFlatQuad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outwardHint)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-16f) return;
            n = SafeUnit(n, outwardHint);
            if (Vector3.Dot(n, outwardHint) < 0f) { (b, d) = (d, b); n = -n; }

            int i0 = AddVertex(a, n, new Vector2(0f, 0f));
            int i1 = AddVertex(b, n, new Vector2(1f, 0f));
            int i2 = AddVertex(c, n, new Vector2(1f, 1f));
            int i3 = AddVertex(d, n, new Vector2(0f, 1f));
            var t = Tri(sub);
            t.Add(i0); t.Add(i1); t.Add(i2);
            t.Add(i0); t.Add(i2); t.Add(i3);
        }

        // ------------------------------------------------------------------ chamfered box ---
        /// <summary>
        /// The workhorse. A box with all 12 edges and 8 corners cut, so every silhouette edge
        /// picks up a highlight. chamfer is in world units and is clamped to something sane.
        /// </summary>
        public void AddChamferBox(int sub, Vector3 center, Vector3 size, float chamfer,
                                  Quaternion? rotation = null)
        {
            Vector3 h = size * 0.5f;
            float c = Mathf.Min(chamfer, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.85f);
            c = Mathf.Max(0f, c);
            Quaternion rot = rotation ?? Quaternion.identity;

            Vector3 P(float x, float y, float z) => center + rot * new Vector3(x, y, z);
            Vector3 D(float x, float y, float z) => rot * new Vector3(x, y, z);

            float[] hs = { h.x, h.y, h.z };

            // --- 6 inset faces ---
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int s = -1; s <= 1; s += 2)
                {
                    float[] q = new float[3];
                    q[axis] = s * hs[axis];

                    Vector3[] corners = new Vector3[4];
                    for (int su = -1; su <= 1; su += 2)
                        for (int sv = -1; sv <= 1; sv += 2)
                        {
                            // Order round the quad rather than in raster order.
                            int idx = (su == -1) ? (sv == -1 ? 0 : 1) : (sv == -1 ? 3 : 2);
                            float[] p = (float[])q.Clone();
                            p[u] = su * (hs[u] - c);
                            p[v] = sv * (hs[v] - c);
                            corners[idx] = P(p[0], p[1], p[2]);
                        }

                    float[] nrm = new float[3];
                    nrm[axis] = s;
                    AddFlatQuad(sub, corners[0], corners[1], corners[2], corners[3],
                                D(nrm[0], nrm[1], nrm[2]));
                }
            }

            if (c <= 1e-5f) return;

            // --- 12 edge chamfers ---
            for (int w = 0; w < 3; w++)
            {
                int u = (w + 1) % 3, v = (w + 2) % 3;
                for (int su = -1; su <= 1; su += 2)
                    for (int sv = -1; sv <= 1; sv += 2)
                    {
                        float[] a = new float[3], b = new float[3], d2 = new float[3], e = new float[3];

                        a[w] = -(hs[w] - c); a[u] = su * hs[u]; a[v] = sv * (hs[v] - c);
                        b[w] = +(hs[w] - c); b[u] = su * hs[u]; b[v] = sv * (hs[v] - c);
                        d2[w] = +(hs[w] - c); d2[u] = su * (hs[u] - c); d2[v] = sv * hs[v];
                        e[w] = -(hs[w] - c); e[u] = su * (hs[u] - c); e[v] = sv * hs[v];

                        float[] nrm = new float[3];
                        nrm[u] = su; nrm[v] = sv;

                        AddFlatQuad(sub, P(a[0], a[1], a[2]), P(b[0], b[1], b[2]),
                                         P(d2[0], d2[1], d2[2]), P(e[0], e[1], e[2]),
                                    D(nrm[0], nrm[1], nrm[2]));
                    }
            }

            // --- 8 corner triangles ---
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 A = P(sx * h.x, sy * (h.y - c), sz * (h.z - c));
                        Vector3 B = P(sx * (h.x - c), sy * h.y, sz * (h.z - c));
                        Vector3 C = P(sx * (h.x - c), sy * (h.y - c), sz * h.z);
                        AddFlatTri(sub, A, B, C, D(sx, sy, sz));
                    }
        }

        // ----------------------------------------------------------------------- revolves ---
        /// <summary>Tapered cylinder / cone / drum. r1 = 0 makes a cone.</summary>
        public void AddCylinder(int sub, Vector3 baseCenter, float r0, float r1, float height,
                                int sides, bool capBottom = true, bool capTop = true,
                                Quaternion? rotation = null)
        {
            sides = Mathf.Max(3, sides);
            Quaternion rot = rotation ?? Quaternion.identity;
            Vector3 up = rot * Vector3.up;

            Vector3 Ring(float r, float y, int i)
            {
                float a = (i / (float)sides) * Mathf.PI * 2f;
                return baseCenter + rot * new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }

            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Vector3 b0 = Ring(r0, 0f, i), b1 = Ring(r0, 0f, j);
                Vector3 t0 = Ring(r1, height, i), t1 = Ring(r1, height, j);
                Vector3 hint = ((b0 + b1 + t0 + t1) * 0.25f - (baseCenter + up * (height * 0.5f))).normalized;

                if (r1 <= 1e-5f) AddFlatTri(sub, b0, b1, t0, hint);
                else if (r0 <= 1e-5f) AddFlatTri(sub, b0, t1, t0, hint);
                else AddFlatQuad(sub, b0, b1, t1, t0, hint);
            }

            if (capBottom && r0 > 1e-5f)
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    AddFlatTri(sub, baseCenter, Ring(r0, 0f, i), Ring(r0, 0f, j), -up);
                }

            if (capTop && r1 > 1e-5f)
            {
                Vector3 topC = baseCenter + up * height;
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    AddFlatTri(sub, topC, Ring(r1, height, i), Ring(r1, height, j), up);
                }
            }
        }

        /// <summary>
        /// Faceted blob built from a subdivided octahedron then pushed around by seeded noise.
        /// Used for boulders, foliage clumps and the character head.
        /// </summary>
        public void AddBlob(int sub, Vector3 center, Vector3 radius, int subdivisions,
                            float lumpiness, int seed, bool flat = true)
        {
            var rnd = new System.Random(seed);
            var basePts = new List<Vector3>();
            var faces = new List<int[]>();
            BuildOctahedron(basePts, faces);
            for (int s = 0; s < Mathf.Clamp(subdivisions, 0, 3); s++) Subdivide(basePts, faces);

            // Per-direction lump offsets, stable for a given seed.
            var offs = new float[basePts.Count];
            for (int i = 0; i < basePts.Count; i++)
            {
                Vector3 d = basePts[i].normalized;
                float n = Mathf.PerlinNoise(d.x * 1.9f + seed * 0.31f, d.z * 1.9f + d.y * 1.3f + seed * 0.77f);
                offs[i] = 1f + (n - 0.5f) * 2f * lumpiness + (float)(rnd.NextDouble() - 0.5) * lumpiness * 0.35f;
            }

            Vector3 Pt(int i)
            {
                Vector3 d = basePts[i].normalized * offs[i];
                return center + new Vector3(d.x * radius.x, d.y * radius.y, d.z * radius.z);
            }

            if (flat)
            {
                foreach (var f in faces)
                {
                    Vector3 a = Pt(f[0]), b = Pt(f[1]), c = Pt(f[2]);
                    AddFlatTri(sub, a, b, c, ((a + b + c) / 3f - center).normalized);
                }
            }
            else
            {
                int baseIndex = verts.Count;
                for (int i = 0; i < basePts.Count; i++)
                {
                    Vector3 p = Pt(i);
                    AddVertex(p, (p - center).normalized, new Vector2(0.5f, 0.5f));
                }
                var t = Tri(sub);
                foreach (var f in faces)
                {
                    Vector3 a = Pt(f[0]), b = Pt(f[1]), c = Pt(f[2]);
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    bool flip = Vector3.Dot(n, (a + b + c) / 3f - center) < 0f;
                    t.Add(baseIndex + f[0]);
                    t.Add(baseIndex + (flip ? f[2] : f[1]));
                    t.Add(baseIndex + (flip ? f[1] : f[2]));
                }
            }
        }

        static void BuildOctahedron(List<Vector3> pts, List<int[]> faces)
        {
            pts.Clear(); faces.Clear();
            pts.Add(Vector3.up); pts.Add(Vector3.down);
            pts.Add(Vector3.forward); pts.Add(Vector3.back);
            pts.Add(Vector3.right); pts.Add(Vector3.left);
            faces.Add(new[] { 0, 2, 4 }); faces.Add(new[] { 0, 4, 3 });
            faces.Add(new[] { 0, 3, 5 }); faces.Add(new[] { 0, 5, 2 });
            faces.Add(new[] { 1, 4, 2 }); faces.Add(new[] { 1, 3, 4 });
            faces.Add(new[] { 1, 5, 3 }); faces.Add(new[] { 1, 2, 5 });
        }

        static void Subdivide(List<Vector3> pts, List<int[]> faces)
        {
            var mid = new Dictionary<long, int>();
            var outFaces = new List<int[]>(faces.Count * 4);

            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int idx)) return idx;
                Vector3 m = ((pts[a] + pts[b]) * 0.5f).normalized;
                pts.Add(m);
                idx = pts.Count - 1;
                mid[key] = idx;
                return idx;
            }

            foreach (var f in faces)
            {
                int a = f[0], b = f[1], c = f[2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                outFaces.Add(new[] { a, ab, ca });
                outFaces.Add(new[] { b, bc, ab });
                outFaces.Add(new[] { c, ca, bc });
                outFaces.Add(new[] { ab, bc, ca });
            }
            faces.Clear();
            faces.AddRange(outFaces);
        }

        // ------------------------------------------------------------------- extrusions -----
        /// <summary>Extrude a closed 2D polygon (XY) along Z. Used for the portal arch.</summary>
        public void AddExtrudedRing(int sub, IList<Vector2> outer, IList<Vector2> inner,
                                    float depth, Vector3 offset)
        {
            int n = outer.Count;
            if (n < 3 || inner.Count != n) return;
            float hz = depth * 0.5f;

            Vector3 O(Vector2 p, float z) => offset + new Vector3(p.x, p.y, z);

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;

                // front and back rims
                AddFlatQuad(sub, O(outer[i], -hz), O(outer[j], -hz), O(inner[j], -hz), O(inner[i], -hz),
                            Vector3.back);
                AddFlatQuad(sub, O(outer[i], hz), O(outer[j], hz), O(inner[j], hz), O(inner[i], hz),
                            Vector3.forward);

                // outer wall
                Vector2 mo = (outer[i] + outer[j]) * 0.5f;
                Vector2 mi = (inner[i] + inner[j]) * 0.5f;
                Vector3 outHint = new Vector3(mo.x - mi.x, mo.y - mi.y, 0f).normalized;
                AddFlatQuad(sub, O(outer[i], -hz), O(outer[j], -hz), O(outer[j], hz), O(outer[i], hz),
                            outHint);

                // inner wall
                AddFlatQuad(sub, O(inner[i], -hz), O(inner[j], -hz), O(inner[j], hz), O(inner[i], hz),
                            -outHint);
            }
        }

        // ------------------------------------------------------------------------ output ----
        public void Transform(Matrix4x4 m)
        {
            var nm = m.inverse.transpose;
            for (int i = 0; i < verts.Count; i++)
            {
                verts[i] = m.MultiplyPoint3x4(verts[i]);
                normals[i] = nm.MultiplyVector(normals[i]).normalized;
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);

            var keys = new List<int>(tris.Keys);
            keys.Sort();
            int maxSub = keys.Count > 0 ? keys[keys.Count - 1] + 1 : 1;
            mesh.subMeshCount = maxSub;
            for (int s = 0; s < maxSub; s++)
                mesh.SetTriangles(tris.TryGetValue(s, out var list) ? list : new List<int>(), s);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(false);
            return mesh;
        }
    }
}

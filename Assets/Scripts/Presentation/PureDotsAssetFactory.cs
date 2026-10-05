using UnityEngine;

namespace GameHolder.PureDots
{
    public static class PureDotsAssetFactory
    {
        public static Mesh CreateBottomCenterQuadMesh()
        {
            var mesh = new Mesh { name = "BottomCenterQuad" };

            // Bottom-center pivot at (0, 0, 0): ground contact feet alignment
            var vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0.0f, 0.0f),
                new Vector3( 0.5f, 0.0f, 0.0f),
                new Vector3(-0.5f, 1.0f, 0.0f),
                new Vector3( 0.5f, 1.0f, 0.0f)
            };

            var uvs = new Vector2[]
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 1.0f)
            };

            var normals = new Vector3[]
            {
                -Vector3.forward,
                -Vector3.forward,
                -Vector3.forward,
                -Vector3.forward
            };

            var triangles = new int[]
            {
                0, 2, 1,
                2, 3, 1
            };

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }

        public static Texture2D GenerateProjectileTexture()
        {
            const int size = PresentationConstants.ProjectileTextureSize;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ProjectileTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x / (float)size) * 2.0f - 1.0f;
                    float ny = (y / (float)size) * 2.0f - 1.0f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);

                    if (d <= 0.85f)
                    {
                        float t = 1.0f - (d / 0.85f);
                        Color col = Color.Lerp(new Color(0.1f, 0.9f, 1.0f, 0.0f), new Color(1.0f, 1.0f, 0.8f, 1.0f), t * t);
                        col.a = Mathf.Clamp01(t * 1.5f);
                        pixels[y * size + x] = col;
                    }
                    else
                    {
                        pixels[y * size + x] = Color.clear;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        public static Texture2D GenerateGemAtlasTexture()
        {
            const int atlasSize = PresentationConstants.GemAtlasSize;
            const int half = atlasSize / PresentationConstants.GemAtlasGridSize;
            var tex = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false)
            {
                name = "GemAtlasTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[atlasSize * atlasSize];

            Color[] tierColors = new Color[]
            {
                new Color(0.2f, 1.0f, 0.4f, 1.0f),  // Tier 0: Emerald Green
                new Color(0.2f, 0.8f, 1.0f, 1.0f),  // Tier 1: Sapphire Blue
                new Color(0.9f, 0.3f, 1.0f, 1.0f),  // Tier 2: Amethyst Purple
                new Color(1.0f, 0.85f, 0.1f, 1.0f)  // Tier 3: Radiant Gold
            };

            for (int quad = 0; quad < PresentationConstants.GemAtlasGridSize * PresentationConstants.GemAtlasGridSize; quad++)
            {
                int ox = (quad % PresentationConstants.GemAtlasGridSize) * half;
                int oy = (quad / PresentationConstants.GemAtlasGridSize) * half;
                Color baseCol = tierColors[quad];

                for (int y = 0; y < half; y++)
                {
                    for (int x = 0; x < half; x++)
                    {
                        float nx = (x / (float)half) * 2.0f - 1.0f;
                        float ny = (y / (float)half) * 2.0f - 1.0f;
                        // Faceted diamond shape: |nx| + |ny| <= 0.8
                        float manhattan = Mathf.Abs(nx) + Mathf.Abs(ny);

                        int px = ox + x;
                        int py = oy + y;

                        if (manhattan <= 0.8f)
                        {
                            // Inner diamond facets
                            float facet = Mathf.Clamp01(1.0f - manhattan / 0.8f);
                            Color c = Color.Lerp(baseCol * 0.6f, baseCol, facet);

                            // Specular glint in top-left
                            if (nx < 0.1f && ny > 0.1f && manhattan < 0.5f)
                            {
                                c = Color.Lerp(c, Color.white, 0.7f);
                            }

                            pixels[py * atlasSize + px] = c;
                        }
                        else
                        {
                            pixels[py * atlasSize + px] = Color.clear;
                        }
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        public static Material CreateSpriteMaterial(Texture2D mainTexture)
        {
            var shader = Shader.Find("PureDots/SpriteDOTS");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var mat = new Material(shader)
            {
                enableInstancing = true,
                mainTexture = mainTexture
            };
            mat.SetTexture("_MainTex", mainTexture);
            mat.SetTexture("_BaseMap", mainTexture);
            mat.SetFloat("_Cutoff", PresentationConstants.SpriteAlphaCutoff);
            mat.SetVector("_SpriteUV", new Vector4(1, 1, 0, 0));
            mat.SetVector("_BaseColor", new Vector4(1, 1, 1, 1));
            return mat;
        }
    }
}

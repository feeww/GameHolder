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

        public static Texture2D GeneratePlayerTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PlayerSpriteTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x / (float)size) * 2.0f - 1.0f; // -1 to 1
                    float ny = (y / (float)size);             // 0 to 1

                    // Triangle ship shape pointing up
                    float halfWidth = 0.85f * (1.0f - ny * 0.85f);
                    if (Mathf.Abs(nx) <= halfWidth && ny >= 0.1f && ny <= 0.95f)
                    {
                        // Body gradient
                        float dFromCenter = Mathf.Abs(nx) / Mathf.Max(0.01f, halfWidth);
                        Color hullColor = Color.Lerp(new Color(0.1f, 0.6f, 0.95f, 1.0f), new Color(0.05f, 0.2f, 0.5f, 1.0f), dFromCenter);

                        // Cockpit canopy
                        if (Mathf.Abs(nx) < 0.25f * (1.0f - ny) && ny > 0.45f && ny < 0.85f)
                        {
                            hullColor = new Color(0.85f, 0.98f, 1.0f, 1.0f);
                        }
                        // Engine glow at bottom
                        else if (ny < 0.25f && Mathf.Abs(nx) < 0.4f)
                        {
                            hullColor = new Color(0.3f, 0.95f, 1.0f, 1.0f);
                        }

                        pixels[y * size + x] = hullColor;
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

        public static Texture2D GenerateEnemyTexture(bool isTank)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = isTank ? "EnemyTankTex" : "EnemyRunnerTex",
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

                    if (isTank)
                    {
                        // Bulky octagonal armored juggernaut
                        float octagon = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny)) + 0.45f * Mathf.Min(Mathf.Abs(nx), Mathf.Abs(ny));
                        if (octagon < 0.85f)
                        {
                            Color armorColor = Color.Lerp(new Color(0.8f, 0.15f, 0.15f, 1.0f), new Color(0.25f, 0.05f, 0.05f, 1.0f), octagon);
                            // Eye slit in center
                            if (Mathf.Abs(ny) < 0.15f && Mathf.Abs(nx) < 0.6f)
                            {
                                armorColor = new Color(1.0f, 0.8f, 0.2f, 1.0f);
                            }
                            pixels[y * size + x] = armorColor;
                        }
                        else
                        {
                            pixels[y * size + x] = Color.clear;
                        }
                    }
                    else
                    {
                        // Sleek aggressive dart / runner shape
                        float widthAtY = 0.8f * (1.0f - (ny + 1.0f) * 0.48f);
                        if (Mathf.Abs(nx) <= widthAtY && ny >= -0.8f && ny <= 0.85f)
                        {
                            Color runnerColor = Color.Lerp(new Color(1.0f, 0.45f, 0.05f, 1.0f), new Color(0.6f, 0.1f, 0.0f, 1.0f), Mathf.Abs(nx) / Mathf.Max(0.01f, widthAtY));
                            // Glowing inner spine
                            if (Mathf.Abs(nx) < 0.15f)
                            {
                                runnerColor = new Color(1.0f, 0.95f, 0.4f, 1.0f);
                            }
                            pixels[y * size + x] = runnerColor;
                        }
                        else
                        {
                            pixels[y * size + x] = Color.clear;
                        }
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        public static Texture2D GenerateEnemyRangedSkirmisherTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "EnemySkirmisherTex",
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

                    // Diamond winged hovercraft silhouette
                    float diamond = Mathf.Abs(nx) / 0.85f + Mathf.Abs(ny) / 0.75f;
                    if (diamond <= 1.0f)
                    {
                        // Violet/magenta hull gradient
                        Color hullColor = Color.Lerp(new Color(0.75f, 0.2f, 0.95f, 1.0f), new Color(0.25f, 0.05f, 0.38f, 1.0f), diamond);

                        // Cyan pulse core / emitter in center
                        float coreDist = Mathf.Sqrt(nx * nx + ny * ny);
                        if (coreDist < 0.25f)
                        {
                            hullColor = Color.Lerp(new Color(0.2f, 0.95f, 1.0f, 1.0f), Color.white, 1.0f - coreDist / 0.25f);
                        }
                        // Wingtip stabilizers
                        else if (Mathf.Abs(nx) > 0.6f && Mathf.Abs(ny) < 0.25f)
                        {
                            hullColor = new Color(0.3f, 0.9f, 1.0f, 1.0f);
                        }

                        pixels[y * size + x] = hullColor;
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

        public static Texture2D GenerateEnemyRangedSniperTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "EnemySniperTex",
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

                    bool inBarrel = Mathf.Abs(nx) <= 0.14f && ny >= -0.92f && ny <= 0.25f;
                    bool inBase = (Mathf.Abs(nx) <= 0.72f * (1.0f - (ny - 0.05f) * 0.45f)) && ny >= 0.0f && ny <= 0.85f;

                    if (inBarrel || inBase)
                    {
                        // Dark emerald / heavy ballistic alloy
                        Color metalColor = Color.Lerp(new Color(0.18f, 0.48f, 0.42f, 1.0f), new Color(0.06f, 0.18f, 0.16f, 1.0f), Mathf.Abs(nx));

                        // Amber / gold targeting optics in core
                        float opticsDist = Mathf.Sqrt(nx * nx + (ny - 0.2f) * (ny - 0.2f));
                        if (opticsDist < 0.22f)
                        {
                            metalColor = Color.Lerp(new Color(1.0f, 0.85f, 0.15f, 1.0f), Color.white, 1.0f - opticsDist / 0.22f);
                        }
                        // Railgun energy coil rings along barrel
                        else if (inBarrel && (Mathf.Abs(ny - -0.3f) < 0.06f || Mathf.Abs(ny - -0.6f) < 0.06f))
                        {
                            metalColor = new Color(1.0f, 0.6f, 0.1f, 1.0f);
                        }

                        pixels[y * size + x] = metalColor;
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

        public static Texture2D GenerateProjectileTexture()
        {
            const int size = 32;
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
            const int atlasSize = 128;
            const int half = 64;
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

            for (int quad = 0; quad < 4; quad++)
            {
                int ox = (quad % 2) * half;
                int oy = (quad / 2) * half;
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
            mat.SetFloat("_Cutoff", 0.3f);
            mat.SetVector("_SpriteUV", new Vector4(1, 1, 0, 0));
            mat.SetVector("_BaseColor", new Vector4(1, 1, 1, 1));
            return mat;
        }
    }
}

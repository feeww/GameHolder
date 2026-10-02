using UnityEngine;
using UnityEngine.UI;

namespace GameHolder.PureDots
{
    // One UI mesh per text block. Numeric changes reuse character and glyph storage.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BatchedHudText : Graphic
    {
        private const string Glyphs = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
        private readonly char[] m_Text = new char[512];
        private readonly CharacterInfo[] m_Glyphs = new CharacterInfo[128];
        private Font m_Font;
        private int m_Length;
        public override Texture mainTexture => m_Font != null ? m_Font.material.mainTexture : Texture2D.whiteTexture;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_Font.RequestCharactersInTexture(Glyphs, 14, FontStyle.Normal);
            RefreshGlyphs(m_Font);
            Font.textureRebuilt += RefreshGlyphs;
        }
        private void RefreshGlyphs(Font font)
        {
            if (font != m_Font) return;
            for (int i = 32; i < 127; i++) m_Font.GetCharacterInfo((char)i, out m_Glyphs[i], 14, FontStyle.Normal);
            SetVerticesDirty();
            SetMaterialDirty();
        }
        public void SetText(string text)
        {
            int length = Mathf.Min(text.Length, m_Text.Length);
            bool changed = length != m_Length;
            for (int i = 0; i < length; i++) { changed |= m_Text[i] != text[i]; m_Text[i] = text[i]; }
            if (!changed) return;
            m_Length = length; SetVerticesDirty();
        }
        public void SetText(char[] text, int length)
        {
            length = Mathf.Clamp(length, 0, Mathf.Min(text.Length, m_Text.Length));
            bool changed = length != m_Length;
            for (int i = 0; i < length; i++) { changed |= m_Text[i] != text[i]; m_Text[i] = text[i]; }
            if (!changed) return;
            m_Length = length; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Rect rect = rectTransform.rect;
            float x = rect.xMin, y = rect.yMax - 17;
            for (int i = 0; i < m_Length; i++)
            {
                char character = m_Text[i];
                if (character == '\n') { x = rect.xMin; y -= 26; continue; }
                if (character < 32 || character >= 127) continue;
                CharacterInfo glyph = m_Glyphs[character];
                if (character != ' ')
                {
                    int start = vertices.currentVertCount;
                    vertices.AddVert(new Vector3(x + glyph.minX, y + glyph.maxY), color, glyph.uvTopLeft);
                    vertices.AddVert(new Vector3(x + glyph.maxX, y + glyph.maxY), color, glyph.uvTopRight);
                    vertices.AddVert(new Vector3(x + glyph.maxX, y + glyph.minY), color, glyph.uvBottomRight);
                    vertices.AddVert(new Vector3(x + glyph.minX, y + glyph.minY), color, glyph.uvBottomLeft);
                    vertices.AddTriangle(start, start + 1, start + 2);
                    vertices.AddTriangle(start + 2, start + 3, start);
                }
                x += glyph.advance;
            }
        }
        protected override void OnDestroy()
        { Font.textureRebuilt -= RefreshGlyphs; base.OnDestroy(); }
    }
}

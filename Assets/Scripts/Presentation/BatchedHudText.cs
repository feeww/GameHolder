using UnityEngine;
using UnityEngine.UI;

namespace GameHolder.PureDots
{
    // One UI mesh per text block. Numeric changes reuse character and glyph storage.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BatchedHudText : Graphic
    {
        private const string Glyphs = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
        private const int AsciiTableSize = 128, FirstPrintableAscii = 32, PrintableAsciiEnd = 127;
        private readonly char[] m_Text = new char[PresentationConstants.HudTextCapacity];
        private readonly CharacterInfo[] m_Glyphs = new CharacterInfo[AsciiTableSize];
        private Font m_Font;
        private int m_Length;
        public override Texture mainTexture => m_Font != null ? m_Font.material.mainTexture : Texture2D.whiteTexture;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_Font.RequestCharactersInTexture(Glyphs, PresentationConstants.HudFontSize, FontStyle.Normal);
            RefreshGlyphs(m_Font);
            Font.textureRebuilt += RefreshGlyphs;
        }
        private void RefreshGlyphs(Font font)
        {
            if (font != m_Font) return;
            for (int i = FirstPrintableAscii; i < PrintableAsciiEnd; i++) m_Font.GetCharacterInfo((char)i, out m_Glyphs[i], PresentationConstants.HudFontSize, FontStyle.Normal);
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
            float x = rect.xMin, y = rect.yMax - PresentationConstants.HudFontBaseline;
            for (int i = 0; i < m_Length; i++)
            {
                char character = m_Text[i];
                if (character == '\n') { x = rect.xMin; y -= PresentationConstants.HudLineHeight; continue; }
                if (character < FirstPrintableAscii || character >= PrintableAsciiEnd) continue;
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

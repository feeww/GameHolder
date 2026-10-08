using UnityEngine;

namespace GameHolder.PureDots
{
    public sealed class TemporaryZonePresentation : MonoBehaviour
    {
        private GameObject[] m_Views;
        private GameObject[] m_Indicators, m_Arrows;
        private TextMesh[] m_Timers;
        private Mesh m_Mesh;
        private Mesh m_ArrowMesh;
        private Material m_StructureMaterial, m_RingMaterial, m_ArrowMaterial;
        private Camera m_Camera;
        private TemporaryZoneSettings m_Settings;
        private int[] m_Seconds, m_HoldTenths;
        private bool[] m_Offscreen;

        public void Initialize(TemporaryZoneSettings settings, Camera camera)
        {
            m_Camera = camera;
            if (settings == null || !settings.Enabled) return;
            m_Settings = settings;
            m_Mesh = PureDotsAssetFactory.CreateBottomCenterQuadMesh();
            m_StructureMaterial = PureDotsAssetFactory.CreateSpriteMaterial(settings.StructureTexture != null ? settings.StructureTexture : Texture2D.whiteTexture);
            m_StructureMaterial.SetVector("_BaseColor", settings.StructureTint);
            var uv = settings.StructureUV;
            m_StructureMaterial.SetVector("_SpriteUV", new Vector4(uv.width, uv.height, uv.x, uv.y));
            m_RingMaterial = PureDotsAssetFactory.CreateSpriteMaterial(Texture2D.whiteTexture);
            m_RingMaterial.SetVector("_BaseColor", settings.ZoneColor);
            m_ArrowMaterial = PureDotsAssetFactory.CreateSpriteMaterial(settings.ArrowTexture != null ? settings.ArrowTexture : Texture2D.whiteTexture);
            m_ArrowMaterial.SetVector("_BaseColor", settings.ArrowColor);
            m_ArrowMesh = PureDotsAssetFactory.CreateBottomCenterQuadMesh();
            var vertices = m_ArrowMesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i].y -= .5f;
            m_ArrowMesh.vertices = vertices;
            if (settings.ArrowTexture == null)
            {
                m_ArrowMesh.Clear();
                m_ArrowMesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, 0), new Vector3(-.5f, .5f) };
                m_ArrowMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                m_ArrowMesh.triangles = new[] { 0, 2, 1 };
            }
            m_ArrowMesh.RecalculateBounds();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var circle = new Vector3[64];
            for (int i = 0; i < circle.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / circle.Length;
                circle[i] = new Vector3(Mathf.Cos(angle) * settings.Radius, Mathf.Sin(angle) * settings.Radius, 0);
            }
            m_Views = new GameObject[settings.MaxActiveZones];
            m_Indicators = new GameObject[m_Views.Length]; m_Arrows = new GameObject[m_Views.Length];
            m_Timers = new TextMesh[m_Views.Length]; m_Seconds = new int[m_Views.Length];
            m_HoldTenths = new int[m_Views.Length]; m_Offscreen = new bool[m_Views.Length];
            for (int i = 0; i < m_Views.Length; i++)
            {
                var view = new GameObject("Temporary zone " + (i + 1)); view.transform.SetParent(transform, false);
                m_Views[i] = view;
                var column = new GameObject("Column", typeof(MeshFilter), typeof(MeshRenderer));
                column.transform.SetParent(view.transform, false);
                column.transform.localScale = new Vector3(settings.StructureSize.x, settings.StructureSize.y, 1);
                column.GetComponent<MeshFilter>().sharedMesh = m_Mesh;
                column.GetComponent<MeshRenderer>().sharedMaterial = m_StructureMaterial;
                var ring = new GameObject("Capture ring", typeof(LineRenderer)); ring.transform.SetParent(view.transform, false);
                ring.transform.localPosition = new Vector3(0, 0, .01f);
                var line = ring.GetComponent<LineRenderer>(); line.useWorldSpace = false; line.loop = true;
                line.sharedMaterial = m_RingMaterial; line.startWidth = line.endWidth = settings.RingWidth;
                line.positionCount = circle.Length; line.SetPositions(circle);
                view.SetActive(false);
                var indicator = new GameObject("Zone indicator " + (i + 1)); indicator.transform.SetParent(transform, false);
                m_Indicators[i] = indicator;
                var arrow = new GameObject("Direction arrow", typeof(MeshFilter), typeof(MeshRenderer));
                arrow.transform.SetParent(indicator.transform, false); m_Arrows[i] = arrow;
                arrow.GetComponent<MeshFilter>().sharedMesh = m_ArrowMesh;
                arrow.GetComponent<MeshRenderer>().sharedMaterial = m_ArrowMaterial;
                var timer = new GameObject("Zone timer", typeof(TextMesh)); timer.transform.SetParent(indicator.transform, false);
                var text = timer.GetComponent<TextMesh>(); m_Timers[i] = text;
                text.font = font; text.fontSize = PresentationConstants.HudFontSize;
                text.anchor = TextAnchor.UpperCenter; text.alignment = TextAlignment.Center; text.color = settings.ZoneColor;
                timer.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                m_Seconds[i] = -1;
                indicator.SetActive(false);
            }
        }

        public void ApplySnapshot(SimulationSnapshot snapshot)
        {
            if (m_Views == null || m_Camera == null) return;
            for (int i = 0; i < m_Views.Length; i++)
            {
                bool active = snapshot.Player.IsDead == 0 && i < snapshot.Zone.Active.Length;
                if (m_Views[i].activeSelf != active) m_Views[i].SetActive(active);
                if (!active) m_Indicators[i].SetActive(false);
                if (!active) continue;
                var position = snapshot.Zone.Active[i].Position;
                m_Views[i].transform.position = new Vector3(position.x, position.y,
                    PresentationDepth.Calculate(position.y, m_Camera.transform.position.y));
                var zone = snapshot.Zone.Active[i];
                var screen = m_Camera.WorldToScreenPoint(new Vector3(position.x, position.y, 0));
                bool offscreen = screen.z <= 0 || !m_Camera.pixelRect.Contains(screen);
                active = !offscreen || m_Settings.ShowOffscreenArrow;
                m_Indicators[i].SetActive(active);
                if (!active) continue;
                Vector2 direction = (Vector2)screen - m_Camera.pixelRect.center;
                if (screen.z <= 0) direction = -direction;
                Vector2 point = offscreen ? IndicatorPosition(m_Camera.pixelRect.center + direction, m_Camera.pixelRect, m_Settings.IndicatorMargin) : (Vector2)screen;
                float depth = Mathf.Max(1, m_Camera.nearClipPlane + 1);
                var world = m_Camera.ScreenToWorldPoint(new Vector3(point.x, point.y, depth));
                float pixelSize = Vector3.Distance(world, m_Camera.ScreenToWorldPoint(new Vector3(point.x + 1, point.y, depth)));
                m_Indicators[i].transform.SetPositionAndRotation(world, m_Camera.transform.rotation);
                var arrow = m_Arrows[i]; arrow.SetActive(offscreen);
                arrow.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                float aspect = m_Settings.ArrowTexture != null ? (float)m_Settings.ArrowTexture.height / m_Settings.ArrowTexture.width : 1;
                arrow.transform.localScale = new Vector3(m_Settings.ArrowSize * pixelSize, m_Settings.ArrowSize * aspect * pixelSize, 1);
                var timer = m_Timers[i];
                timer.characterSize = pixelSize * 10;
                timer.transform.localPosition = new Vector3(0, -(offscreen ? m_Settings.ArrowSize * .5f + 8 : 50) * pixelSize, -.01f);
                int seconds = Mathf.CeilToInt(zone.Remaining), hold = Mathf.FloorToInt(zone.HoldTime * 10);
                if (m_Seconds[i] != seconds || m_HoldTenths[i] != hold || m_Offscreen[i] != offscreen)
                {
                    timer.text = offscreen ? $"{seconds}s" : $"{m_Settings.ZoneName}\n{seconds}s | Hold {hold / 10f:0.0} / {m_Settings.HoldDuration:0.0}s";
                    m_Seconds[i] = seconds; m_HoldTenths[i] = hold; m_Offscreen[i] = offscreen;
                }
            }
        }

        public static Vector2 IndicatorPosition(Vector2 target, Rect viewport, float margin)
        {
            Vector2 half = viewport.size * .5f;
            half -= Vector2.one * Mathf.Min(margin, Mathf.Max(0, Mathf.Min(half.x, half.y) - 1));
            Vector2 direction = target - viewport.center;
            float ratio = Mathf.Max(Mathf.Abs(direction.x) / Mathf.Max(1, half.x), Mathf.Abs(direction.y) / Mathf.Max(1, half.y));
            return viewport.center + direction / Mathf.Max(1, ratio);
        }

        private void OnDestroy()
        {
            GamePresentationBootstrap.DestroyOwned(m_Mesh);
            GamePresentationBootstrap.DestroyOwned(m_StructureMaterial);
            GamePresentationBootstrap.DestroyOwned(m_RingMaterial);
            GamePresentationBootstrap.DestroyOwned(m_ArrowMesh);
            GamePresentationBootstrap.DestroyOwned(m_ArrowMaterial);
        }
    }

}

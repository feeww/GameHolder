using UnityEditor;
using UnityEngine;

namespace GameHolder.PureDots.Editor
{
    public class PureDotsShaderGUI : ShaderGUI
    {
        public static string Description(string property) => property switch {
            "_MainTex" => "Texture sampled by the sprite or tiled floor. Empty uses the shader's default texture.",
            "_Cutoff" => "Pixels with alpha below this value are discarded. 0 preserves all alpha values.",
            "_BaseColor" => "Color multiplied with sprite artwork; simulation rendering supplies per-entity overrides.",
            "_SpriteUV" => "Normalized texture crop: x/y are width/height, z/w are the lower-left offset.",
            "_TileScale" => "World units per floor tile. Values below 0.1 are rendered as 0.1.",
            "_OriginTileOffset" => "Runtime floating-origin offset in world units. Set by the floor presentation controller.",
            "_FloorColor" => "Color multiplied with floor texture samples.",
            "_GridColor" => "Color of grid lines drawn at floor tile boundaries.",
            _ => string.Empty };

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            foreach (var property in properties)
                if ((property.propertyFlags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) == 0)
                    editor.ShaderProperty(property, new GUIContent(property.displayName, Description(property.name)));
            editor.RenderQueueField();
            editor.EnableInstancingField();
            editor.DoubleSidedGIField();
        }
    }
}

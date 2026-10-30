using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameJam.UITools
{
    /// <summary>Small 16x16 pixel icons for the overlay, drawn from rectangles so no image assets are needed.</summary>
    static class UIIcons
    {
        struct Box
        {
            public int x, y, w, h;
            public float a;
            public Box(int x, int y, int w, int h, float a = 1f) { this.x = x; this.y = y; this.w = w; this.h = h; this.a = a; }
            public Box Transposed => new Box(y, x, h, w, a);
        }

        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static bool cachedForProSkin;

        public static Texture2D Get(string id)
        {
            if (cachedForProSkin != EditorGUIUtility.isProSkin)
            {
                cache.Clear();
                cachedForProSkin = EditorGUIUtility.isProSkin;
            }

            if (cache.TryGetValue(id, out var tex) && tex != null) return tex;
            tex = Build(Shape(id));
            cache[id] = tex;
            return tex;
        }

        // Coordinates are from the bottom-left corner. Vertical variants are transposes of horizontal ones.
        static Box[] Shape(string id)
        {
            switch (id)
            {
                case "align-left": return new[] { new Box(1, 1, 1, 14), new Box(3, 9, 10, 4), new Box(3, 3, 6, 4) };
                case "align-center": return new[] { new Box(7, 0, 2, 16, 0.45f), new Box(2, 9, 12, 4), new Box(4, 3, 8, 4) };
                case "align-right": return new[] { new Box(14, 1, 1, 14), new Box(3, 9, 10, 4), new Box(7, 3, 6, 4) };
                case "align-top": return Transpose(Shape("align-right"));
                case "align-middle": return Transpose(Shape("align-center"));
                case "align-bottom": return Transpose(Shape("align-left"));
                case "distribute-h":
                    return new[]
                    {
                        new Box(0, 0, 1, 16, 0.45f), new Box(15, 0, 1, 16, 0.45f),
                        new Box(2, 4, 3, 8), new Box(7, 2, 2, 12), new Box(11, 5, 3, 6)
                    };
                case "distribute-v": return Transpose(Shape("distribute-h"));
                case "stack-h": return new[] { new Box(1, 4, 4, 8), new Box(6, 4, 4, 8), new Box(11, 4, 4, 8) };
                case "stack-v": return Transpose(Shape("stack-h"));
                case "match-w":
                    return new[]
                    {
                        new Box(1, 3, 1, 10), new Box(14, 3, 1, 10), new Box(2, 7, 12, 2),
                        new Box(3, 6, 1, 4), new Box(12, 6, 1, 4)
                    };
                case "match-h": return Transpose(Shape("match-w"));
                default: return new[] { new Box(2, 2, 12, 12, 0.5f) };
            }
        }

        static Box[] Transpose(Box[] boxes)
        {
            var result = new Box[boxes.Length];
            for (var i = 0; i < boxes.Length; i++) result[i] = boxes[i].Transposed;
            return result;
        }

        static Texture2D Build(Box[] boxes)
        {
            const int size = 16;
            var ink = EditorGUIUtility.isProSkin ? new Color(0.86f, 0.86f, 0.86f) : new Color(0.16f, 0.16f, 0.16f);
            var pixels = new Color[size * size];
            foreach (var b in boxes)
                for (var y = b.y; y < b.y + b.h && y < size; y++)
                for (var x = b.x; x < b.x + b.w && x < size; x++)
                {
                    var c = ink;
                    c.a = Mathf.Max(pixels[y * size + x].a, b.a);
                    pixels[y * size + x] = c;
                }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}

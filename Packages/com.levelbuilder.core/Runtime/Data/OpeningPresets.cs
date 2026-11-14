using System.Collections.Generic;

namespace LevelBuilder
{
    public static class OpeningPresets
    {
        public struct Preset
        {
            public string name;
            public OpeningType type;
            public float width, height, sill;
        }

        static readonly Preset[] Doors =
        {
            new Preset { name = "Puerta", type = OpeningType.Door, width = 0.9f, height = 2.1f, sill = 0f },
            new Preset { name = "Puerta doble", type = OpeningType.Door, width = 1.6f, height = 2.1f, sill = 0f }
        };
        static readonly Preset[] Windows =
        {
            new Preset { name = "Ventana", type = OpeningType.Window, width = 1.2f, height = 1.2f, sill = 0.9f },
            new Preset { name = "Ventana alta", type = OpeningType.Window, width = 0.6f, height = 0.6f, sill = 1.6f }
        };
        static readonly Preset[] Empties =
        {
            new Preset { name = "Hueco", type = OpeningType.Empty, width = 0.9f, height = 2.1f, sill = 0f }
        };

        public static IReadOnlyList<Preset> For(OpeningType t) =>
            t == OpeningType.Door ? Doors : t == OpeningType.Window ? Windows : Empties;

        public static WallOpening Create(Preset p, float center) => new WallOpening
        {
            id = IdUtil.New(),
            type = p.type,
            center = center,
            width = p.width,
            height = p.height,
            sill = p.sill
        };
    }
}

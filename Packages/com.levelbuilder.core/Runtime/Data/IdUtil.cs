using System;

namespace LevelBuilder
{
    public static class IdUtil
    {
        public static string New() => Guid.NewGuid().ToString("N");
    }
}

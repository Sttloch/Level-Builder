using NUnit.Framework;

namespace LevelBuilder.Tests
{
    public class IdUtilTests
    {
        [Test]
        public void New_ReturnsDistinct32CharIds()
        {
            var a = IdUtil.New(); var b = IdUtil.New();
            Assert.AreEqual(32, a.Length);
            Assert.AreNotEqual(a, b);
        }
    }
}

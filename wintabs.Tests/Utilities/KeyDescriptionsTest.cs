
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace wintabs.Utilities.Tests
{
    [TestClass]
    public class KeyDescriptionsTest
    {
        [TestMethod]
        public void TestCaseConversion()
        {
            Assert.AreEqual("Browser Back", KeyDescriptions.GetDescription(KeyCode.BrowserBack));
        }
    }
}

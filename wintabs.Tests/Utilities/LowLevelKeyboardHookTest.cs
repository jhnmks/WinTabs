
using Microsoft.VisualStudio.TestTools.UnitTesting;

using wintabs.Utilities;
using System.Threading.Tasks;

namespace wintabs.Tests.Utilities
{
    [TestClass]
    public class LowLevelKeyboardHookTest
    {
        [TestMethod]
        public void TestNewDispose()
        {
            var llkbh = new LowLevelKeyboardHook();
            llkbh.Dispose();
        }
    }
}

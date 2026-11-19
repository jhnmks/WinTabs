using System.Windows.Input;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using wintabs.Utilities;

namespace wintabs.Tests.Utilities
{
    [TestClass]
    public class DelegateCommandTest
    {
        [TestMethod]
        public void CanExecuteAndExecuteTolerateUnresolvedCommandParameter()
        {
            string? executed = null;
            ICommand command = new DelegateCommand<string>(value => executed = value);

            Assert.IsFalse(command.CanExecute(null));
            Assert.IsFalse(command.CanExecute(42));
            command.Execute(null);
            command.Execute(42);
            Assert.IsNull(executed);

            Assert.IsTrue(command.CanExecute("tab"));
            command.Execute("tab");
            Assert.AreEqual("tab", executed);
        }
    }
}

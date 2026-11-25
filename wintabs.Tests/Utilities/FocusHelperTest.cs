using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using wintabs.Utilities;

namespace wintabs.Tests.Utilities
{
    [TestClass]
    public class FocusHelperTest
    {
        [TestMethod]
        public async Task AcceptedRequestIsVerifiedAndRetriedIfNecessary()
        {
            IntPtr current = new(1), target = new(2);
            int attempts = 0;
            var result = await FocusHelper.ActivateTabAsync(target, () => current, hwnd =>
            {
                if (++attempts == 2) current = hwnd;
                return true;
            }, () => true, () => Task.CompletedTask);
            Assert.AreEqual(FocusHelper.ActivationResult.Activated, result);
            Assert.AreEqual(2, attempts);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task NewRequestOrForegroundSwitchCancelsRetry(bool newRequest)
        {
            IntPtr current = new(1);
            int attempts = 0;
            bool valid = true;
            var result = await FocusHelper.ActivateTabAsync(new(2), () => current, _ => { attempts++; return false; },
                () => valid, () =>
                {
                    if (newRequest) valid = false;
                    else current = new(3);
                    return Task.CompletedTask;
                });
            Assert.AreEqual(FocusHelper.ActivationResult.Superseded, result);
            Assert.AreEqual(1, attempts);
        }

        [TestMethod]
        public async Task PersistentRefusalStopsAfterOneRetry()
        {
            int attempts = 0;
            var result = await FocusHelper.ActivateTabAsync(new(2), () => new(1), _ => { attempts++; return true; },
                () => true, () => Task.CompletedTask);
            Assert.AreEqual(FocusHelper.ActivationResult.Refused, result);
            Assert.AreEqual(2, attempts);
        }
    }
}

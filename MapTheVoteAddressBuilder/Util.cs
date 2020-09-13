using OpenQA.Selenium;
using System.Threading.Tasks;
using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;
using System.Runtime.InteropServices;

#if NETCORE
using System;
using System.Reflection;
#endif

namespace MapTheVoteAddressBuilder
{
    public enum ErrorPhase
    {
        DriverInitialization,
        MapTheVoteLogin,
        AddressSelection,
        ApplicationSubmit,
        ApplicationConfirm,
        ButtonClick,
        MarkApplicationProcessed,
        Misc,
        ParsingArguments
    }

    public enum ElementSearchType
    {
        ID,
        ClassName,
        Name
    }

    public static class Util
    {
        // Won't actually mark things as complete or submit applications.
        public static bool DebugMode { get; set; } = false;

        static Random _rng = new Random();

        public static void PreventSleep()
        {
            SetThreadExecutionState(ExecutionState.EsContinuous | ExecutionState.EsSystemRequired);
        }

        public static void AllowSleep()
        {
            SetThreadExecutionState(ExecutionState.EsContinuous);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

        [FlagsAttribute]
        private enum ExecutionState : uint
        {
            EsAwaymodeRequired = 0x00000040,
            EsContinuous = 0x80000000,
            EsDisplayRequired = 0x00000002,
            EsSystemRequired = 0x00000001
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos( IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);

        private const int HWND_TOPMOST = -1;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOSIZE = 0x0001;

        public static void SetAlwaysOnTop()
        {
            IntPtr hWnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

            SetWindowPos(hWnd, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }

        public static void LogError(ErrorPhase aWarningType, string aErrorMessage)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERROR: [{aWarningType}] - {aErrorMessage}");
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static async Task<bool> ClickOnElement(this RemoteWebDriver aDriver, string id, ElementSearchType searchType = ElementSearchType.ID)
        {
            bool success = false;
            try
            {
                // Ensure first that the element exists on screen.
                var elementToClick = aDriver.WaitForElement(id, searchType);

                if (elementToClick != null)
                {
                    // Now ensure that it's clickable.
                    var clickWait = new WebDriverWait(aDriver, TimeSpan.FromSeconds(2));
                    clickWait.Until(ExpectedConditions.ElementToBeClickable(elementToClick));
                    elementToClick.Click();

                    // This is a final "catch all" wait in case someone after us doesn't properly wait (like a script).
                    // TBH it's probably unnecessary, but can be removed in the future.
                    await Util.RandomWait(250, 400);

                    success = true;
                }
            }
            catch (Exception e)
            {
                Util.LogError(ErrorPhase.ButtonClick, e.ToString());
            }

            return success;
        }

        public static IWebElement WaitForElement(this RemoteWebDriver aDriver, string id, ElementSearchType searchType = ElementSearchType.ID)
        {
            IWebElement foundElement = null;
            try
            {
                Func<IWebDriver, IWebElement> checkElementExists = null;

                switch (searchType)
                {
                    case ElementSearchType.ID:
                    {
                        checkElementExists = ExpectedConditions.ElementExists(By.Id(id));
                        break;
                    }

                    case ElementSearchType.ClassName:
                    {
                        checkElementExists = ExpectedConditions.ElementExists(By.ClassName(id));
                        break;
                    }

                    case ElementSearchType.Name:
                    {
                        checkElementExists = ExpectedConditions.ElementExists(By.Name(id));
                        break;
                    }

                    default:
                    {
                        throw new NotSupportedException("Invalid ElementSearch type.");
                    }
                }

                // Ensure first that the element exists on screen.
                var btnWait = new WebDriverWait(aDriver, TimeSpan.FromSeconds(10));
                foundElement = btnWait.Until(checkElementExists);
            }
            catch (Exception) // We're cool bubbling this up to the calling function.
            {

            }

            return foundElement;
        }

        public static async Task RandomWait(uint aBaseMs, uint aVarianceMs = 0u)
        {
            var waitAmount = _rng.Next((int)aBaseMs, (int)(aBaseMs + aVarianceMs));
            if (waitAmount > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(waitAmount));   
            }
        }

        // Workaround for an issue in .NET Core: https://stackoverflow.com/a/48223937
        public static void FixDriverCommandExecutionDelay(IWebDriver driver)
        {
#if NETCORE
            PropertyInfo commandExecutorProperty = typeof(RemoteWebDriver).GetProperty("CommandExecutor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetProperty);
            ICommandExecutor commandExecutor = (ICommandExecutor)commandExecutorProperty.GetValue(driver);

            FieldInfo remoteServerUriField = commandExecutor.GetType().GetField("remoteServerUri", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.SetField);

            if (remoteServerUriField == null)
            {
                FieldInfo internalExecutorField = commandExecutor.GetType().GetField("internalExecutor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField);
                commandExecutor = (ICommandExecutor)internalExecutorField.GetValue(commandExecutor);
                remoteServerUriField = commandExecutor.GetType().GetField("remoteServerUri", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.SetField);
            }

            if (remoteServerUriField != null)
            {
                string remoteServerUri = remoteServerUriField.GetValue(commandExecutor).ToString();

                string localhostUriPrefix = "http://localhost";

                if (remoteServerUri.StartsWith(localhostUriPrefix))
                {
                    remoteServerUri = remoteServerUri.Replace(localhostUriPrefix, "http://127.0.0.1");

                    remoteServerUriField.SetValue(commandExecutor, new Uri(remoteServerUri));
                }
            }
#endif // NETCORE
        }

    }
}

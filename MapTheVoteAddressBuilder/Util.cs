using OpenQA.Selenium;
using System.Threading.Tasks;
using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;

#if NETCORE
using System;
using System.Reflection;
#endif

namespace MapTheVoteAddressBuilder
{
    public enum ErrorPhase
    {
        AddressSelection,
        ApplicationSubmit,
        ApplicationConfirm,
        ButtonClick,
        MarkApplicationProcessed,
        Misc
    }

    public static class Util
    {
        static Random _rng = new Random();

        public static void LogError(ErrorPhase aWarningType, string aErrorMessage)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERROR: [{aWarningType}] - {aErrorMessage}");
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static async Task<bool> ClickOnButton(this RemoteWebDriver aDriver, string id)
        {
            bool success = false;
            try
            {
                var registerBtn = aDriver.FindElementById(id);

                var btnWait = new WebDriverWait(aDriver, TimeSpan.FromSeconds(10));
                btnWait.Until(ExpectedConditions.ElementToBeClickable(registerBtn));
                registerBtn.Click();

                await Util.RandomWait(250, 400);

                success = true;
            }
            catch (Exception e)
            {
                Util.LogError(ErrorPhase.ButtonClick, e.ToString());
            }

            return success;
        }

        public static IWebElement WaitForElement(this RemoteWebDriver aDriver, string elementName, double aTimeout = 3.0)
        {
            var _wait = new WebDriverWait(aDriver, TimeSpan.FromSeconds(aTimeout));
            return _wait.Until(d => d.FindElement(By.Name(elementName)));
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

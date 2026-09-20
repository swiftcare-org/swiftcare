using OpenQA.Selenium;

namespace E2ETests.Support;

public static class Browser
{
    // Sets an <input> value the way React notices. Selenium's SendKeys is
    // unreliable for <input type="date"> (locale-dependent parsing); assigning
    // through the native value setter and dispatching input/change events drives
    // React's controlled-component onChange without depending on keyboard locale.
    public static void SetInputValue(IWebDriver driver, IWebElement element, string value)
    {
        ((IJavaScriptExecutor)driver).ExecuteScript(
            """
            const el = arguments[0], value = arguments[1];
            const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value').set;
            setter.call(el, value);
            el.dispatchEvent(new Event('input', { bubbles: true }));
            el.dispatchEvent(new Event('change', { bubbles: true }));
            """,
            element, value);
    }
}

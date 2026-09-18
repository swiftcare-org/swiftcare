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

    // Establishes SWC-92's browser-side precondition without calling QueueService's
    // shared Call Next operation. SWC-22 already covers that operation end to end; using
    // it again in a parallel test can consume a queue row another test is observing.
    // This helper stores the same CurrentPatient shape that DoctorDashboard writes after
    // a successful call, scoped to the authenticated Doctor and clinic-local date.
    public static void StoreCurrentPatientAssignment(IWebDriver driver, SeededPatient patient)
    {
        ((IJavaScriptExecutor)driver).ExecuteScript(
            """
            const user = JSON.parse(sessionStorage.getItem('swiftcare.auth.user'));
            if (!user || user.role !== 'Doctor') {
              throw new Error('A Doctor must be signed in before storing a current patient.');
            }

            const storageKey = `swiftcare.doctor.current-patient.${user.userId}.${arguments[2]}`;
            const assignment = {
              queueId: '00000000-0000-0000-0000-000000000110',
              patientId: arguments[0],
              queueNumber: 'Q-110',
              status: 'IN_CONSULTATION',
              doctorId: user.userId,
              doctorName: user.fullName,
              roomNumber: user.roomNumber,
              calledAt: new Date().toISOString(),
              patientName: arguments[1],
            };

            sessionStorage.setItem(storageKey, JSON.stringify(assignment));
            """,
            patient.PatientId,
            patient.FullName,
            ClinicClock.TodayIsoDate());

        driver.Navigate().Refresh();
    }
}

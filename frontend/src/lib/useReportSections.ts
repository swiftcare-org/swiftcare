import { useEffect, useState } from 'react';

// What one service returned for one period. `data` is null when the request failed.
// Keeping the period with the result means a section is "loading" whenever its result is
// for a different period than the one selected, with no extra state to keep in step.
interface SectionResult<TData> {
  period: string;
  data: TData | null;
}

interface ReportSections<TActivity, TPrescriptions> {
  /** True until both services have answered for the selected period. */
  loading: boolean;
  /** Null while loading, and null afterwards when the activity request failed. */
  activity: TActivity | null;
  /** Null while loading, and null afterwards when the prescription request failed. */
  prescriptions: TPrescriptions | null;
}

// Loads the two halves of a summary report for one period (a date or a month). The two
// services are asked separately, so one being down never hides the other section.
// Pass module-level loader functions: a new function each render would reload every render.
export function useReportSections<TActivity, TPrescriptions>(
  period: string,
  loadActivity: (period: string) => Promise<TActivity>,
  loadPrescriptions: (period: string) => Promise<TPrescriptions>,
): ReportSections<TActivity, TPrescriptions> {
  const [activity, setActivity] = useState<SectionResult<TActivity> | null>(null);
  const [prescriptions, setPrescriptions] = useState<SectionResult<TPrescriptions> | null>(null);

  useEffect(() => {
    if (!period) {
      return;
    }

    let disposed = false;

    void loadActivity(period).then(
      (data) => !disposed && setActivity({ period, data }),
      () => !disposed && setActivity({ period, data: null }),
    );
    void loadPrescriptions(period).then(
      (data) => !disposed && setPrescriptions({ period, data }),
      () => !disposed && setPrescriptions({ period, data: null }),
    );

    return () => {
      disposed = true;
    };
  }, [period, loadActivity, loadPrescriptions]);

  const activityResult = activity?.period === period ? activity : null;
  const prescriptionResult = prescriptions?.period === period ? prescriptions : null;

  return {
    loading: Boolean(period) && (!activityResult || !prescriptionResult),
    activity: activityResult?.data ?? null,
    prescriptions: prescriptionResult?.data ?? null,
  };
}

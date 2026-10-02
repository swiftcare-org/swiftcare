import type { Prescription } from '../api/prescriptions';
import { StatusBadge } from '../components/ui/StatusBadge';
import { formatDateTime } from '../lib/format';

/** Shown wherever a patient has no prescriptions; the wording is set by SWC-42. */
export const NO_PRESCRIPTIONS_MESSAGE = 'No prescriptions recorded for this patient';

interface PrescriptionHistoryListProps {
  /** Already ordered newest first by the API. */
  prescriptions: readonly Prescription[];
  /** The prescription being written now, if it is in the list, so it is not mistaken for an earlier one. */
  currentPrescriptionId?: string;
}

// A patient's past prescriptions, read-only. Used on the patient history page and beside
// the prescription form, so a doctor sees the same record in both places.
export function PrescriptionHistoryList({ prescriptions, currentPrescriptionId }: PrescriptionHistoryListProps) {
  return (
    <div className="space-y-4">
      {prescriptions.map((prescription) => (
        <article key={prescription.id} className="rounded-md border border-slate-200 p-4">
          <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
            <p className="font-semibold text-slate-900">{formatDateTime(prescription.createdAt)}</p>
            {prescription.id === currentPrescriptionId && (
              <StatusBadge tone="info" className="mr-auto">
                This visit
              </StatusBadge>
            )}
            <StatusBadge
              tone={prescription.status === 'DISPENSED' ? 'success' : 'warning'}
              data-testid="prescription-status"
            >
              {prescription.status}
            </StatusBadge>
          </div>
          <p className="mt-1 text-sm text-slate-600">Prescribed by {prescription.doctorName}</p>
          <ul className="mt-3 space-y-2">
            {prescription.medicines.map((medicine) => (
              <li
                key={medicine.id}
                className="break-words border-l-2 border-slate-200 pl-3 text-sm text-slate-700"
              >
                <span className="font-semibold text-slate-900" data-testid="history-medicine-name">
                  {medicine.medicineName}
                </span>
                : {medicine.dosage}, {medicine.frequency}, {medicine.duration}
                {medicine.instructions && `. ${medicine.instructions}`}
              </li>
            ))}
          </ul>
        </article>
      ))}
    </div>
  );
}

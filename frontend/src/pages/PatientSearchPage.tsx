import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { DashboardShell } from '../dashboards/DashboardShell';
import { searchPatients } from '../api/patients';
import type { PatientSearchResult } from '../api/patients';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';

type SearchStatus = 'idle' | 'searching' | 'results' | 'empty' | 'error';

// Matches PatientSearchService.MinimumTermLength on the server: below this, the server
// itself returns an empty array, so the client simply never calls for a shorter term.
const MINIMUM_TERM_LENGTH = 2;
const DEBOUNCE_MS = 300;

const GENERIC_ERROR_MESSAGE = 'Unable to search. Please try again.';

export function PatientSearchPage() {
  const { user } = useAuth();
  const backRoute = user ? roleRoutes[user.role] : '/login';

  const [term, setTerm] = useState('');
  const [status, setStatus] = useState<SearchStatus>('idle');
  const [results, setResults] = useState<PatientSearchResult[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Guards against an older, slower request overwriting a newer one's results.
  const latestRequestId = useRef(0);

  useEffect(() => {
    const trimmedTerm = term.trim();

    if (trimmedTerm.length < MINIMUM_TERM_LENGTH) {
      latestRequestId.current += 1;
      setStatus('idle');
      setResults([]);
      setErrorMessage(null);
      return;
    }

    const requestId = ++latestRequestId.current;
    const timeoutId = window.setTimeout(() => {
      setStatus('searching');
      searchPatients(trimmedTerm)
        .then((found) => {
          if (latestRequestId.current !== requestId) {
            return;
          }
          setResults(found);
          setErrorMessage(null);
          setStatus(found.length > 0 ? 'results' : 'empty');
        })
        .catch((error) => {
          if (latestRequestId.current !== requestId) {
            return;
          }
          setResults([]);
          setStatus('error');
          if (error instanceof ApiError && error.status === 403) {
            setErrorMessage('You are not authorized to search patients.');
          } else {
            setErrorMessage(GENERIC_ERROR_MESSAGE);
          }
        });
    }, DEBOUNCE_MS);

    return () => window.clearTimeout(timeoutId);
  }, [term]);

  return (
    <DashboardShell sectionLabel="Patient Search">
      <Link
        to={backRoute}
        className="mt-4 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
      >
        ← Back to Dashboard
      </Link>

      <div className="mt-6 border border-slate-300 bg-white px-6 py-6">
        <label htmlFor="patientSearch" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
          Search by Name, NIC, or Phone Number
        </label>
        <input
          id="patientSearch"
          name="patientSearch"
          type="search"
          autoComplete="off"
          value={term}
          onChange={(event) => setTerm(event.target.value)}
          className="mt-1.5 block w-full border-2 border-slate-400 bg-white px-3 py-2.5 text-sm text-slate-900 focus:border-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
        />
      </div>

      <div aria-live="polite" className="mt-6">
        {status === 'searching' && <p className="text-sm text-slate-500">Searching…</p>}

        {status === 'error' && errorMessage && (
          <div className="border-t-4 border-b border-red-700 bg-red-50 px-6 py-3">
            <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">Search Failed</p>
            <p className="mt-1 text-sm text-red-900">{errorMessage}</p>
          </div>
        )}

        {status === 'empty' && (
          <div className="border-t-4 border-b border-slate-400 bg-slate-50 px-6 py-3">
            <p className="text-sm text-slate-700">No patients found. Would you like to register?</p>
            <Link
              to="/reception/patients/new"
              className="mt-2 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
            >
              Register a new patient
            </Link>
          </div>
        )}

        {status === 'results' && (
          <div className="overflow-x-auto border border-slate-300">
            <table className="min-w-full divide-y divide-slate-200 text-sm">
              <thead className="bg-slate-50">
                <tr>
                  <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                    Full Name
                  </th>
                  <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                    NIC
                  </th>
                  <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                    Phone
                  </th>
                  <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                    Blood Group
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200">
                {results.map((result) => (
                  <tr key={result.patientId}>
                    <td className="px-4 py-2 text-slate-900">{result.fullName}</td>
                    <td className="px-4 py-2 text-slate-700">{result.nic}</td>
                    <td className="px-4 py-2 text-slate-700">{result.phoneNumber}</td>
                    <td className="px-4 py-2 text-slate-700">{result.bloodGroup}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </DashboardShell>
  );
}

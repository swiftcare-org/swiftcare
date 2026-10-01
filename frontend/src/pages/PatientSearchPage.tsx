import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { DashboardShell } from '../dashboards/DashboardShell';
import { searchPatients } from '../api/patients';
import type { PatientSearchResult } from '../api/patients';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';
import { Banner } from '../components/ui/Banner';
import { LoadingText } from '../components/ui/Feedback';
import { Field } from '../components/ui/Field';
import { SectionCard } from '../components/ui/SectionCard';
import {
  tableBodyClassName,
  tableCellClassName,
  tableClassName,
  tableHeadClassName,
  tableHeaderCellClassName,
  tableWrapperClassName,
  textLinkClassName,
} from '../components/ui/table';

type SearchStatus = 'idle' | 'results' | 'empty' | 'error';

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
  // Separate from status so the previous results stay on screen while the next search runs.
  const [isSearching, setIsSearching] = useState(false);

  // Guards against an older, slower request overwriting a newer one's results.
  const latestRequestId = useRef(0);

  useEffect(() => {
    const trimmedTerm = term.trim();

    if (trimmedTerm.length < MINIMUM_TERM_LENGTH) {
      latestRequestId.current += 1;
      setStatus('idle');
      setResults([]);
      setErrorMessage(null);
      setIsSearching(false);
      return;
    }

    const requestId = ++latestRequestId.current;
    const timeoutId = window.setTimeout(() => {
      setIsSearching(true);
      searchPatients(trimmedTerm)
        .then((found) => {
          if (latestRequestId.current !== requestId) {
            return;
          }
          setIsSearching(false);
          setResults(found);
          setErrorMessage(null);
          setStatus(found.length > 0 ? 'results' : 'empty');
        })
        .catch((error) => {
          if (latestRequestId.current !== requestId) {
            return;
          }
          setIsSearching(false);
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
    <DashboardShell sectionLabel="Patient Search" backLink={{ to: backRoute, destination: 'Dashboard' }}>
      <SectionCard>
        <Field
          id="patientSearch"
          label="Search by Name, NIC, or Phone Number"
          hint={`Type at least ${MINIMUM_TERM_LENGTH} characters. Results appear as you type.`}
        >
          {(control) => (
            <input
              {...control}
              name="patientSearch"
              type="search"
              autoComplete="off"
              value={term}
              onChange={(event) => setTerm(event.target.value)}
            />
          )}
        </Field>
      </SectionCard>

      <div aria-live="polite" className="space-y-3">
        {isSearching && <LoadingText>Searching…</LoadingText>}

        {status === 'error' && errorMessage && (
          <Banner tone="error" title="Search Failed">
            {errorMessage}
          </Banner>
        )}

        {status === 'empty' && (
          <Banner tone="neutral" title="No Results">
            {user?.role === 'Receptionist' ? (
              <>
                <p>No patients found. Would you like to register?</p>
                <p className="mt-1">
                  <Link to="/reception/patients/new" className={textLinkClassName}>
                    Register a new patient
                  </Link>
                </p>
              </>
            ) : (
              <p>No patients found.</p>
            )}
          </Banner>
        )}

        {status === 'results' && (
          <>
            <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">
              {results.length} {results.length === 1 ? 'patient' : 'patients'} found
            </p>
            <div className={`${tableWrapperClassName} ${isSearching ? 'opacity-60' : ''}`}>
              <table className={tableClassName}>
                <thead className={tableHeadClassName}>
                  <tr>
                    {['Full Name', 'NIC', 'Phone', 'Blood Group'].map((heading) => (
                      <th key={heading} scope="col" className={tableHeaderCellClassName}>
                        {heading}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className={tableBodyClassName}>
                  {results.map((result) => (
                    <tr key={result.patientId}>
                      <td className={tableCellClassName}>
                        <Link to={`/patients/${result.patientId}`} className={textLinkClassName}>
                          {result.fullName}
                        </Link>
                      </td>
                      <td className={tableCellClassName}>{result.nic}</td>
                      <td className={tableCellClassName}>{result.phoneNumber}</td>
                      <td className={tableCellClassName}>{result.bloodGroup}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </DashboardShell>
  );
}

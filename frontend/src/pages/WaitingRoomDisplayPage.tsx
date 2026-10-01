import { useEffect, useState } from 'react';
import {
  getWaitingRoomDisplay,
  type WaitingRoomDisplay,
} from '../api/queue';
import { formatTime } from '../lib/format';
import swiftcareLogo from '../assets/swiftcare-logo.png';

type DisplayLoadState = 'loading' | 'loaded' | 'error';

const POLL_INTERVAL_MS = 5_000;

export function WaitingRoomDisplayPage() {
  const [loadState, setLoadState] = useState<DisplayLoadState>('loading');
  const [display, setDisplay] = useState<WaitingRoomDisplay>({
    currentRooms: [],
    nextQueueNumbers: [],
  });
  const [refreshFailed, setRefreshFailed] = useState(false);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<string | null>(null);

  useEffect(() => {
    document.title = 'Waiting Room Display · SwiftCare';
  }, []);

  useEffect(() => {
    let disposed = false;
    let requestInFlight = false;
    let hasLoaded = false;

    async function refreshDisplay() {
      if (requestInFlight) {
        return;
      }

      requestInFlight = true;

      try {
        const latestDisplay = await getWaitingRoomDisplay();
        if (disposed) {
          return;
        }

        setDisplay(latestDisplay);
        setRefreshFailed(false);
        setLastUpdatedAt(new Date().toISOString());
        setLoadState('loaded');
        hasLoaded = true;
      } catch {
        if (disposed) {
          return;
        }

        setRefreshFailed(true);
        if (!hasLoaded) {
          setLoadState('error');
        }
      } finally {
        requestInFlight = false;
      }
    }

    const initialLoadId = window.setTimeout(() => void refreshDisplay(), 0);
    const pollId = window.setInterval(() => void refreshDisplay(), POLL_INTERVAL_MS);

    return () => {
      disposed = true;
      window.clearTimeout(initialLoadId);
      window.clearInterval(pollId);
    };
  }, []);

  return (
    <main className="min-h-screen bg-slate-950 text-white">
      <header className="border-b border-slate-700 bg-white px-4 py-4 sm:px-8 lg:px-12">
        <div className="mx-auto flex max-w-[100rem] flex-wrap items-center justify-between gap-4">
          <img src={swiftcareLogo} alt="SwiftCare" width={603} height={176} className="h-10 w-auto sm:h-12 2xl:h-16" />
          <div className="text-right">
            <p className="text-xs font-bold uppercase tracking-[0.18em] text-brand-blue 2xl:text-sm">
              Live Queue
            </p>
            <h1 className="mt-1 text-lg font-semibold text-slate-900 sm:text-2xl 2xl:text-3xl">
              Waiting Room Display
            </h1>
          </div>
        </div>
      </header>

      <div className="mx-auto grid max-w-[100rem] gap-8 px-4 py-8 sm:px-8 lg:min-h-[calc(100vh-7rem)] lg:grid-cols-[3fr_2fr] lg:px-12 lg:py-12">
        <section aria-labelledby="current-rooms-heading">
          <p className="text-sm font-bold uppercase tracking-[0.18em] text-display-accent 2xl:text-base">Now Serving</p>
          <h2 id="current-rooms-heading" className="mt-2 text-2xl font-semibold sm:text-3xl 2xl:text-4xl">
            Current Rooms
          </h2>

          <div aria-live="polite" className="mt-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {loadState === 'loading' && (
              <p className="col-span-full text-xl text-slate-300">Loading live queue…</p>
            )}

            {loadState === 'error' && (
              <p className="col-span-full text-xl text-amber-300">Live queue unavailable. Retrying…</p>
            )}

            {loadState === 'loaded' && display.currentRooms.length === 0 && (
              <div className="col-span-full border border-slate-700 bg-slate-900 px-6 py-8">
                <p className="text-xl text-slate-300">No rooms are serving a patient right now.</p>
              </div>
            )}

            {display.currentRooms.map((room) => (
              <article
                key={room.roomNumber}
                className="border-t-8 border-brand-blue bg-white px-6 py-7 text-slate-950"
              >
                <p className="text-sm font-bold uppercase tracking-[0.12em] text-slate-600 2xl:text-lg">
                  Room {room.roomNumber}
                </p>
                <p className="mt-4 text-5xl font-black tracking-tight sm:text-6xl xl:text-7xl 2xl:text-8xl">
                  {room.queueNumber}
                </p>
                <p className="mt-4 text-lg font-semibold text-brand-blue 2xl:text-2xl">Please proceed →</p>
              </article>
            ))}
          </div>
        </section>

        <section
          aria-labelledby="next-queue-heading"
          className="border border-slate-700 bg-slate-900 px-6 py-7 sm:px-8 lg:self-start"
        >
          <p className="text-sm font-bold uppercase tracking-[0.18em] text-amber-300 2xl:text-base">Coming Up</p>
          <h2 id="next-queue-heading" className="mt-2 text-2xl font-semibold sm:text-3xl 2xl:text-4xl">
            Next in Queue
          </h2>

          <div aria-live="polite" className="mt-7">
            {loadState === 'loading' && <p className="text-xl text-slate-300">Loading…</p>}

            {loadState === 'error' && <p className="text-xl text-amber-300">Waiting for live data…</p>}

            {loadState === 'loaded' && display.nextQueueNumbers.length === 0 && (
              <p className="text-xl text-slate-300">No one is waiting.</p>
            )}

            {display.nextQueueNumbers.length > 0 && (
              <div>
                <ol className="space-y-3">
                  {display.nextQueueNumbers.map((queueNumber, index) => (
                    <li
                      key={queueNumber}
                      className="flex items-center gap-4 border-l-4 border-amber-400 bg-slate-800 px-5 py-4"
                    >
                      <span className="text-base font-bold text-amber-300">{index + 1}</span>
                      <span className="text-4xl font-black tracking-tight sm:text-5xl 2xl:text-6xl">
                        {queueNumber}
                      </span>
                    </li>
                  ))}
                </ol>
              </div>
            )}
          </div>
        </section>

        <footer className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-700 pt-5 text-sm text-slate-400 lg:col-span-2">
          <p>
            Updates automatically every 5 seconds.
            {lastUpdatedAt && ` Last updated ${formatTime(lastUpdatedAt)}.`}
          </p>
          {refreshFailed ? (
            <p role="alert" className="font-semibold text-amber-300">
              Live update unavailable. Retrying…
            </p>
          ) : (
            <p className="flex items-center gap-2 font-semibold text-emerald-300">
              <span className="inline-block h-2.5 w-2.5 bg-emerald-300" aria-hidden="true" />
              Live
            </p>
          )}
        </footer>
      </div>
    </main>
  );
}

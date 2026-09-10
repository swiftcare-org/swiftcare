import { useEffect, useState } from 'react';
import {
  getWaitingRoomDisplay,
  type WaitingRoomDisplay,
} from '../api/queue';
import swiftcareLogo from '../assets/swiftcare-logo.svg';

type DisplayLoadState = 'loading' | 'loaded' | 'error';

const POLL_INTERVAL_MS = 5_000;

export function WaitingRoomDisplayPage() {
  const [loadState, setLoadState] = useState<DisplayLoadState>('loading');
  const [display, setDisplay] = useState<WaitingRoomDisplay>({
    currentRooms: [],
    nextQueueNumbers: [],
  });
  const [refreshFailed, setRefreshFailed] = useState(false);

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
      <header className="border-b border-slate-700 bg-white px-5 py-4 sm:px-8 lg:px-12">
        <div className="mx-auto flex max-w-[100rem] flex-wrap items-center justify-between gap-4">
          <img src={swiftcareLogo} alt="SwiftCare" className="h-9 w-auto sm:h-11" />
          <div className="text-right">
            <p className="text-xs font-bold uppercase tracking-[0.2em] text-brand-blue">
              Live Queue
            </p>
            <h1 className="mt-1 text-lg font-semibold text-slate-900 sm:text-2xl">
              Waiting Room Display
            </h1>
          </div>
        </div>
      </header>

      <div className="mx-auto grid max-w-[100rem] gap-8 px-5 py-8 sm:px-8 lg:min-h-[calc(100vh-7rem)] lg:grid-cols-[3fr_2fr] lg:px-12 lg:py-12">
        <section aria-labelledby="current-rooms-heading">
          <p className="text-sm font-bold uppercase tracking-[0.2em] text-cyan-300">Now Serving</p>
          <h2 id="current-rooms-heading" className="mt-2 text-2xl font-semibold sm:text-3xl">
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
                <p className="text-xl text-slate-300">No rooms currently active</p>
              </div>
            )}

            {display.currentRooms.map((room) => (
              <article
                key={room.roomNumber}
                className="border-t-8 border-cyan-400 bg-white px-6 py-7 text-slate-950 shadow-[0_8px_0_rgba(34,211,238,0.18)]"
              >
                <p className="text-sm font-bold uppercase tracking-[0.16em] text-slate-500">
                  Room {room.roomNumber}
                </p>
                <p className="mt-5 text-5xl font-black tracking-tight sm:text-6xl xl:text-7xl">
                  {room.queueNumber}
                </p>
                <p className="mt-4 text-lg font-semibold text-brand-blue">Please proceed →</p>
              </article>
            ))}
          </div>
        </section>

        <section
          aria-labelledby="next-queue-heading"
          className="border border-slate-700 bg-slate-900 px-6 py-7 sm:px-8 lg:self-start"
        >
          <p className="text-sm font-bold uppercase tracking-[0.2em] text-amber-300">Coming Up</p>
          <h2 id="next-queue-heading" className="mt-2 text-2xl font-semibold sm:text-3xl">
            Next in Queue
          </h2>

          <div aria-live="polite" className="mt-7">
            {loadState === 'loading' && <p className="text-xl text-slate-300">Loading…</p>}

            {loadState === 'error' && <p className="text-xl text-amber-300">Waiting for live data…</p>}

            {loadState === 'loaded' && display.nextQueueNumbers.length === 0 && (
              <p className="text-xl text-slate-300">No queue numbers waiting</p>
            )}

            {display.nextQueueNumbers.length > 0 && (
              <div>
                <p className="text-lg font-semibold text-slate-300">Next:</p>
                <ol className="mt-4 space-y-3">
                  {display.nextQueueNumbers.map((queueNumber, index) => (
                    <li
                      key={queueNumber}
                      className="flex items-center gap-4 border-l-4 border-amber-400 bg-slate-800 px-5 py-4"
                    >
                      <span className="text-base font-bold text-amber-300">{index + 1}</span>
                      <span className="text-4xl font-black tracking-tight sm:text-5xl">
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
          <p>Updates automatically every 5 seconds</p>
          {refreshFailed ? (
            <p role="alert" className="font-semibold text-amber-300">
              Live update unavailable. Retrying…
            </p>
          ) : (
            <p className="font-semibold text-emerald-300">● Live</p>
          )}
        </footer>
      </div>
    </main>
  );
}

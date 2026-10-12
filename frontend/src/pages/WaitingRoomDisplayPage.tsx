import { useEffect, useRef, useState } from 'react';
import {
  getWaitingRoomDisplay,
  type WaitingRoomDisplay,
} from '../api/queue';
import { Icon } from '../components/ui/Icon';
import { formatDate, formatTime } from '../lib/format';
// The dark-background logo: same mark and wordmark, with the tagline in white.
import swiftcareLogo from '../assets/swiftcare-logo-on-dark.png';

type DisplayLoadState = 'loading' | 'loaded' | 'error';

const POLL_INTERVAL_MS = 5_000;
const CLOCK_INTERVAL_MS = 10_000;
// How long a room stays highlighted after a new patient is called to it.
const CALL_HIGHLIGHT_MS = 20_000;

export function WaitingRoomDisplayPage() {
  const [loadState, setLoadState] = useState<DisplayLoadState>('loading');
  const [display, setDisplay] = useState<WaitingRoomDisplay>({
    currentRooms: [],
    nextQueueNumbers: [],
  });
  const [refreshFailed, setRefreshFailed] = useState(false);
  const [now, setNow] = useState(() => new Date().toISOString());
  const [justCalledRooms, setJustCalledRooms] = useState<ReadonlySet<string>>(new Set());
  const previousRooms = useRef<Map<string, string> | null>(null);

  useEffect(() => {
    document.title = 'Waiting Room Display · SwiftCare';
  }, []);

  useEffect(() => {
    const clockId = window.setInterval(() => setNow(new Date().toISOString()), CLOCK_INTERVAL_MS);
    return () => window.clearInterval(clockId);
  }, []);

  useEffect(() => {
    let disposed = false;
    let requestInFlight = false;
    let hasLoaded = false;
    const highlightTimers = new Map<string, number>();

    // A room whose queue number changed since the last poll has just called someone,
    // so it is highlighted for a while to catch that patient's eye.
    function highlightNewCalls(latest: WaitingRoomDisplay) {
      const current = new Map(latest.currentRooms.map((room) => [room.roomNumber, room.queueNumber]));
      const previous = previousRooms.current;
      previousRooms.current = current;

      if (!previous) {
        return;
      }

      for (const [roomNumber, queueNumber] of current) {
        if (previous.get(roomNumber) === queueNumber) {
          continue;
        }

        setJustCalledRooms((rooms) => new Set(rooms).add(roomNumber));
        window.clearTimeout(highlightTimers.get(roomNumber));
        highlightTimers.set(
          roomNumber,
          window.setTimeout(() => {
            setJustCalledRooms((rooms) => {
              const next = new Set(rooms);
              next.delete(roomNumber);
              return next;
            });
          }, CALL_HIGHLIGHT_MS),
        );
      }
    }

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

        highlightNewCalls(latestDisplay);
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
      highlightTimers.forEach((timerId) => window.clearTimeout(timerId));
    };
  }, []);

  const [nextUp, ...laterNumbers] = display.nextQueueNumbers;

  return (
    <main
      className="flex min-h-screen flex-col text-white"
      style={{ background: 'linear-gradient(160deg, #020617 0%, #06122b 55%, #0a2150 100%)' }}
    >
      <header className="border-b border-white/10 px-4 py-4 sm:px-8 lg:px-12">
        <div className="mx-auto flex max-w-[110rem] flex-wrap items-center justify-between gap-4">
          <img src={swiftcareLogo} alt="SwiftCare" width={603} height={176} className="h-10 w-auto sm:h-12 2xl:h-16" />
          <h1 className="sr-only">Waiting Room Display</h1>
          <div className="text-right">
            <p className="text-2xl font-semibold tabular-nums leading-none sm:text-3xl 2xl:text-5xl">
              {formatTime(now)}
            </p>
            <p className="mt-1 text-sm text-slate-400 2xl:text-lg">{formatDate(now)}</p>
          </div>
        </div>
      </header>

      <div className="mx-auto grid w-full max-w-[110rem] flex-1 gap-8 px-4 py-8 sm:px-8 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)] lg:px-12 lg:py-10">
        <section aria-labelledby="current-rooms-heading">
          <div className="flex items-baseline justify-between gap-4">
            <h2 id="current-rooms-heading" className="sr-only">
              Current Rooms
            </h2>
            <p className="text-3xl font-semibold tracking-tight sm:text-4xl 2xl:text-5xl" aria-hidden="true">
              Now Serving
            </p>
            <p className="text-sm text-slate-400 2xl:text-lg">Please go to the room shown with your number</p>
          </div>

          <div aria-live="polite" className="mt-6 grid gap-5 sm:grid-cols-2">
            {loadState === 'loading' && (
              <p className="col-span-full text-xl text-slate-300">Loading live queue…</p>
            )}

            {loadState === 'error' && (
              <p className="col-span-full text-xl text-amber-300">Live queue unavailable. Retrying…</p>
            )}

            {loadState === 'loaded' && display.currentRooms.length === 0 && (
              <div className="col-span-full rounded-xl border border-white/10 bg-white/5 px-6 py-12 text-center">
                <p className="text-2xl font-medium text-slate-200 2xl:text-3xl">
                  No patients are being called right now.
                </p>
                <p className="mt-2 text-lg text-slate-400">Please take a seat. Your number will appear here.</p>
              </div>
            )}

            {display.currentRooms.map((room) => {
              const justCalled = justCalledRooms.has(room.roomNumber);

              return (
                <article
                  key={room.roomNumber}
                  className={`flex flex-col overflow-hidden rounded-xl bg-white text-slate-950 shadow-2xl transition-shadow ${
                    justCalled ? 'ring-4 ring-amber-300' : ''
                  }`}
                >
                  <div className="order-1 flex items-center justify-between px-6 pt-5">
                    <span className="text-sm font-semibold uppercase tracking-wider text-slate-500 2xl:text-lg">
                      Queue number
                    </span>
                    {justCalled && (
                      <span className="animate-pulse rounded-full bg-amber-300 px-3 py-1 text-xs font-bold uppercase tracking-wider text-amber-950 2xl:text-base">
                        Now calling
                      </span>
                    )}
                  </div>
                  {/* The room line comes first in the markup and is shown last, as the instruction. */}
                  <p className="order-3 flex items-center justify-between gap-3 bg-brand-blue px-6 py-4 text-2xl font-semibold text-white sm:text-3xl 2xl:text-5xl">
                    Room {room.roomNumber}
                  </p>
                  <p className="order-2 px-6 pb-5 pt-1 text-6xl font-bold tracking-tight sm:text-7xl 2xl:text-9xl">
                    {room.queueNumber}
                  </p>
                </article>
              );
            })}
          </div>
        </section>

        <section
          aria-labelledby="next-queue-heading"
          className="rounded-xl border border-white/10 bg-white/5 p-6 sm:p-8 lg:self-start"
        >
          <h2 id="next-queue-heading" className="text-2xl font-semibold tracking-tight sm:text-3xl 2xl:text-4xl">
            Next in Queue
          </h2>
          <p className="mt-1 text-sm text-slate-400 2xl:text-lg">Please be ready when your number is close</p>

          <div aria-live="polite" className="mt-6">
            {loadState === 'loading' && <p className="text-xl text-slate-300">Loading…</p>}

            {loadState === 'error' && <p className="text-xl text-amber-300">Waiting for live data…</p>}

            {loadState === 'loaded' && display.nextQueueNumbers.length === 0 && (
              <p className="text-xl text-slate-300">No one is waiting.</p>
            )}

            {nextUp && (
              <ol className="space-y-3">
                <li className="flex items-center justify-between gap-4 rounded-lg bg-amber-300 px-5 py-4 text-amber-950">
                  <span className="text-sm font-bold uppercase tracking-wider 2xl:text-lg">Up next</span>
                  <span className="text-4xl font-bold tracking-tight sm:text-5xl 2xl:text-7xl">
                    {nextUp}
                  </span>
                </li>
                {laterNumbers.map((queueNumber, index) => (
                  <li
                    key={queueNumber}
                    className="flex items-center justify-between gap-4 rounded-lg bg-white/5 px-5 py-3"
                  >
                    <span className="text-base font-medium tabular-nums text-slate-400 2xl:text-xl">{index + 2}</span>
                    <span className="text-3xl font-semibold tracking-tight sm:text-4xl 2xl:text-6xl">
                      {queueNumber}
                    </span>
                  </li>
                ))}
              </ol>
            )}
          </div>
        </section>
      </div>

      <footer className="border-t border-white/10 px-4 py-4 text-sm text-slate-400 sm:px-8 lg:px-12 2xl:text-lg">
        <div className="mx-auto flex max-w-[110rem] flex-wrap items-center justify-between gap-3">
          <p className="flex items-center gap-2">
            <Icon name="display" className="h-4 w-4" />
            This screen updates on its own. You do not need to ask at the desk.
          </p>
          {refreshFailed ? (
            <p role="alert" className="font-semibold text-amber-300">
              Live update unavailable. Retrying…
            </p>
          ) : (
            <p className="flex items-center gap-2 font-semibold text-emerald-300">
              <span className="inline-block h-2.5 w-2.5 animate-pulse rounded-full bg-emerald-300" aria-hidden="true" />
              Live
            </p>
          )}
        </div>
      </footer>
    </main>
  );
}

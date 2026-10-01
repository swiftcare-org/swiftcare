import { Link } from 'react-router-dom';

interface BackLinkProps {
  to: string;
  /** The destination's name, e.g. "Dashboard". Rendered as "Back to Dashboard". */
  destination: string;
}

export function BackLink({ to, destination }: BackLinkProps) {
  return (
    <Link
      to={to}
      className="inline-flex items-center gap-1.5 text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
    >
      <span aria-hidden="true">←</span>
      Back to {destination}
    </Link>
  );
}
